using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Profiling;

public class FogOfWarManager : Singleton<FogOfWarManager>
{
    private static readonly ProfilerMarker FullRefreshMarker =
        new ProfilerMarker("FogOfWar.FullRefresh");
    private static readonly ProfilerMarker FullRefreshBatchMarker =
        new ProfilerMarker("FogOfWar.FullRefreshBatch");
    private static readonly ProfilerMarker RegionalRefreshMarker =
        new ProfilerMarker("FogOfWar.RegionalRefreshBatch");

    [Header("Referências")]
    [SerializeField] private Tilemap fogTilemap;
    [SerializeField] private TileBase blackFogTile; // Tile Escuro (100% Opaco)
    [SerializeField] private TileBase semiFogTile;  // Tile Semitransparente (Degradê)

    [Header("Carregamento incremental")]
    [SerializeField, Min(1)] private int fullRefreshColumnsPerFrame = 16;

    [Header("P5E - Atualização regional")]
    [SerializeField, Min(1)] private int maximumDirtyCellsPerFrame = 2048;
    [SerializeField, Min(1)] private int maximumDirtyCellsPerRegionPass = 256;

    [Header("Configurações do Raio de Visão")]
    [SerializeField] private int innerRadius = 4; // Raio 100% Visível
    [SerializeField] private int outerRadius = 7; // Raio do Degradê/Sombra

    private GridManager subscribedGridManager;
    private readonly Dictionary<int, Queue<int>> pendingCellsByRegion =
        new Dictionary<int, Queue<int>>();
    private readonly Queue<int> pendingRegionOrder = new Queue<int>();
    private readonly HashSet<int> pendingCellKeys = new HashSet<int>();
    private readonly Stack<Queue<int>> cellQueuePool = new Stack<Queue<int>>();
    private bool missingTilemapLogged;

    public int PendingFogCellCount => pendingCellKeys.Count;
    public int PendingFogRegionCount => pendingCellsByRegion.Count;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        subscribedGridManager = GridManager.Instance;
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnGridRebuilt += HandleGridRebuilt;

            if (subscribedGridManager.IsGridReady)
            {
                InitializeFog();
            }
        }
    }

    private void OnDestroy()
    {
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnGridRebuilt -= HandleGridRebuilt;
        }

        ClearPendingVisualChanges();
    }

    private void LateUpdate()
    {
        ProcessPendingVisualChanges();
    }

    private void HandleGridRebuilt()
    {
        ClearPendingVisualChanges();
        if (!SaveGameRuntime.IsLoading)
        {
            InitializeFog();
        }
    }

    public IEnumerator InitializeFogIncrementally()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !TryGetFogTilemap()) yield break;

        ClearPendingVisualChanges();
        fogTilemap.ClearAllTiles();
        int columnsPerFrame = Mathf.Max(1, fullRefreshColumnsPerFrame);

        for (int startX = 0; startX < grid.width; startX += columnsPerFrame)
        {
            int endX = Mathf.Min(startX + columnsPerFrame, grid.width);
            using (FullRefreshBatchMarker.Auto())
            {
                for (int x = startX; x < endX; x++)
                {
                    for (int y = 0; y < grid.height; y++)
                    {
                        Tile tile = grid.GetTile(x, y);
                        if (tile != null)
                        {
                            UpdateFogTileVisual(x, y, tile.fogState);
                        }
                    }
                }
            }

            if (endX < grid.width) yield return null;
        }
    }

    public void InitializeFog()
    {
        if (GridManager.Instance == null || !TryGetFogTilemap()) return;

        ClearPendingVisualChanges();
        long startedAt = PerformanceMetricsService.BeginSample();
        using (FullRefreshMarker.Auto())
        {

        fogTilemap.ClearAllTiles();

        int width = GridManager.Instance.width;
        int height = GridManager.Instance.height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile tile = GridManager.Instance.GetTile(x, y);
                if (tile != null)
                {
                    UpdateFogTileVisual(x, y, tile.fogState);
                }
            }
        }
        }
        PerformanceMetricsService.EndSample(
            PerformanceMetric.FogFullRefresh,
            startedAt);
    }

    public void RevealArea(Vector2Int centerGridPos)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !grid.IsGridReady) return;

        int effectiveOuterRadius = Mathf.Max(0, outerRadius);
        int effectiveInnerRadius = Mathf.Clamp(
            innerRadius,
            0,
            effectiveOuterRadius);
        int outerRadiusSquared = effectiveOuterRadius * effectiveOuterRadius;
        int innerRadiusSquared = effectiveInnerRadius * effectiveInnerRadius;

        for (int x = -effectiveOuterRadius; x <= effectiveOuterRadius; x++)
        {
            for (int y = -effectiveOuterRadius; y <= effectiveOuterRadius; y++)
            {
                int distanceSquared = x * x + y * y;

                if (distanceSquared <= outerRadiusSquared)
                {
                    int targetX = centerGridPos.x + x;
                    int targetY = centerGridPos.y + y;

                    Tile tile = grid.GetTile(targetX, targetY);
                    if (tile == null) continue;
                    FogState previousState = tile.fogState;

                    if (distanceSquared <= innerRadiusSquared)
                    {
                        // Visão total
                        tile.fogState = FogState.Revealed;
                    }
                    else if (tile.fogState != FogState.Revealed)
                    {
                        // Borda em Degradê (Só vira Explored se não tiver sido 100% revelado antes)
                        tile.fogState = FogState.Explored;
                    }

                    if (tile.fogState != previousState)
                    {
                        grid.MarkRegionDirty(
                            targetX,
                            targetY,
                            WorldRegionDirtyFlags.Fog
                            | WorldRegionDirtyFlags.Visual);
                        QueueVisualChange(targetX, targetY);
                    }
                }
            }
        }
    }

    private void QueueVisualChange(int x, int y)
    {
        GridManager grid = subscribedGridManager;
        if (grid == null || !grid.IsGridReady || !grid.IsInsideGrid(x, y)) return;

        int cellKey = x + y * grid.Width;
        if (!pendingCellKeys.Add(cellKey)) return;

        if (!grid.Regions.TryGetByCell(x, y, out WorldRegionState region))
        {
            pendingCellKeys.Remove(cellKey);
            return;
        }

        int regionKey = region.Coordinate.X
            + region.Coordinate.Y * grid.RegionColumns;
        if (!pendingCellsByRegion.TryGetValue(regionKey, out Queue<int> cells))
        {
            cells = RentCellQueue();
            pendingCellsByRegion.Add(regionKey, cells);
            pendingRegionOrder.Enqueue(regionKey);
        }

        cells.Enqueue(cellKey);
    }

    private void ProcessPendingVisualChanges()
    {
        if (pendingCellKeys.Count == 0) return;

        GridManager grid = subscribedGridManager;
        if (grid == null || !grid.IsGridReady || !TryGetFogTilemap()) return;

        int pendingCellsBeforeProcessing = pendingCellKeys.Count;
        int pendingRegionsBeforeProcessing = pendingCellsByRegion.Count;
        int processedCells = 0;
        int budget = Mathf.Max(1, maximumDirtyCellsPerFrame);
        long startedAt = PerformanceMetricsService.BeginSample();

        using (RegionalRefreshMarker.Auto())
        {
            while (processedCells < budget && pendingRegionOrder.Count > 0)
            {
                int regionKey = pendingRegionOrder.Dequeue();
                if (!pendingCellsByRegion.TryGetValue(
                        regionKey,
                        out Queue<int> cells))
                {
                    continue;
                }

                int regionBudget = Mathf.Min(
                    maximumDirtyCellsPerRegionPass,
                    budget - processedCells);
                int regionProcessedCells = 0;

                while (regionProcessedCells < regionBudget && cells.Count > 0)
                {
                    int cellKey = cells.Dequeue();
                    pendingCellKeys.Remove(cellKey);
                    int x = cellKey % grid.Width;
                    int y = cellKey / grid.Width;
                    Tile tile = grid.GetTile(x, y);
                    if (tile != null)
                    {
                        UpdateFogTileVisual(x, y, tile.fogState);
                    }

                    processedCells++;
                    regionProcessedCells++;
                }

                if (cells.Count == 0)
                {
                    pendingCellsByRegion.Remove(regionKey);
                    ReturnCellQueue(cells);
                }
                else
                {
                    pendingRegionOrder.Enqueue(regionKey);
                }
            }
        }

        PerformanceMetricsService.RecordFogRegionalWork(
            startedAt,
            processedCells,
            Mathf.Max(pendingCellsBeforeProcessing, pendingCellKeys.Count),
            Mathf.Max(
                pendingRegionsBeforeProcessing,
                pendingCellsByRegion.Count));
    }

    private Queue<int> RentCellQueue()
    {
        return cellQueuePool.Count > 0
            ? cellQueuePool.Pop()
            : new Queue<int>();
    }

    private void ReturnCellQueue(Queue<int> cells)
    {
        cells.Clear();
        cellQueuePool.Push(cells);
    }

    private void ClearPendingVisualChanges()
    {
        foreach (Queue<int> cells in pendingCellsByRegion.Values)
        {
            ReturnCellQueue(cells);
        }

        pendingCellsByRegion.Clear();
        pendingRegionOrder.Clear();
        pendingCellKeys.Clear();
    }

    private void UpdateFogTileVisual(int x, int y, FogState state)
    {
        if (!TryGetFogTilemap()) return;

        Vector3Int pos = new Vector3Int(x, y, 0);

        switch (state)
        {
            case FogState.Unexplored:
                fogTilemap.SetTile(pos, blackFogTile);
                break;
            case FogState.Explored:
                fogTilemap.SetTile(pos, semiFogTile != null ? semiFogTile : blackFogTile);
                break;
            case FogState.Revealed:
                fogTilemap.SetTile(pos, null); // Remove a névoa para mostrar o mapa normal
                break;
        }
    }

    private bool TryGetFogTilemap()
    {
        if (fogTilemap != null) return true;

        fogTilemap = GetComponent<Tilemap>();
        if (fogTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning("[FogOfWarManager] Fog Tilemap ausente ou destruído; atualização de névoa ignorada.", this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
