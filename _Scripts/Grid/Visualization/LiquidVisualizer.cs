using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Tilemaps;

public class LiquidVisualizer : MonoBehaviour
{
    private static readonly ProfilerMarker ProcessDirtyCellsMarker =
        new ProfilerMarker("LiquidVisualizer.ProcessDirtyCells");
    private static readonly ProfilerMarker FullRefreshMarker =
        new ProfilerMarker("LiquidVisualizer.FullRefresh");
    private static readonly ProfilerMarker FullRefreshBatchMarker =
        new ProfilerMarker("LiquidVisualizer.FullRefreshBatch");
    private static readonly ProfilerMarker DepthCalculationMarker =
        new ProfilerMarker("LiquidVisualizer.DepthCalculation");

    [Header("Referências")]
    [SerializeField] private Tilemap liquidTilemap;

    [Header("Assets de Nível da Água")]
    [SerializeField] private TileBase waterLowTile;
    [SerializeField] private TileBase waterMidTile;
    [SerializeField] private TileBase waterFullTile;

    [Header("Cores do Gradiente por Profundidade")]
    [SerializeField] private Color surfaceWaterColor =
        new Color(0.35f, 0.75f, 1f, 0.45f);
    [SerializeField] private Color deepWaterColor =
        new Color(0.05f, 0.20f, 0.60f, 0.90f);
    [SerializeField, Min(1)] private int maxDepthForGradient = 8;

    [Header("Processamento incremental")]
    [SerializeField, Min(0.01f)] private float visualRefreshInterval = 0.05f;
    [SerializeField, Min(1)] private int maximumDirtyCellsPerRefresh = 2048;
    [SerializeField, Min(1)] private int maximumDepthColumnsPerRefresh = 16;
    [SerializeField, Min(1)] private int fullRefreshColumnsPerFrame = 16;

    public int PendingDirtyCellCount => dirtyCellSet.Count;
    public int PendingDepthColumnCount => dirtyDepthColumnSet.Count;
    public int LastProcessedCellCount { get; private set; }

    private readonly Queue<Vector2Int> dirtyCellQueue =
        new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> dirtyCellSet =
        new HashSet<Vector2Int>();
    private readonly Queue<int> dirtyDepthColumnQueue = new Queue<int>();
    private readonly HashSet<int> dirtyDepthColumnSet = new HashSet<int>();

    private GridManager gridManager;
    private int[,] depthCache;
    private bool missingTilemapLogged;
    private float nextVisualRefreshTime;

    private void Start()
    {
        gridManager = GridManager.Instance != null
            ? GridManager.Instance
            : FindFirstObjectByType<GridManager>();

        if (liquidTilemap == null)
        {
            liquidTilemap = GetComponent<Tilemap>();
        }

        if (gridManager == null)
        {
            return;
        }

        gridManager.OnLiquidChanged += HandleLiquidChanged;
        gridManager.OnTileChanged += HandleTerrainChanged;
        gridManager.OnGridRebuilt += HandleGridRebuilt;

        if (gridManager.IsGridReady)
        {
            FullRefresh();
        }
    }

    private void OnDestroy()
    {
        if (gridManager == null)
        {
            return;
        }

        gridManager.OnLiquidChanged -= HandleLiquidChanged;
        gridManager.OnTileChanged -= HandleTerrainChanged;
        gridManager.OnGridRebuilt -= HandleGridRebuilt;
    }

    private void HandleGridRebuilt()
    {
        if (!SaveGameRuntime.IsLoading)
        {
            FullRefresh();
        }
    }

    public IEnumerator FullRefreshIncrementally()
    {
        if (gridManager == null
            || !gridManager.IsGridReady
            || !TryGetLiquidTilemap())
        {
            yield break;
        }

        ClearDirtyBuffers();
        EnsureDepthCache(true);
        liquidTilemap.ClearAllTiles();
        int columnsPerFrame = Mathf.Max(1, fullRefreshColumnsPerFrame);

        for (int startX = 0; startX < gridManager.width; startX += columnsPerFrame)
        {
            int endX = Mathf.Min(
                startX + columnsPerFrame,
                gridManager.width);

            using (FullRefreshBatchMarker.Auto())
            {
                for (int x = startX; x < endX; x++)
                {
                    RecalculateDepthColumn(x, false);
                    for (int y = 0; y < gridManager.height; y++)
                    {
                        if (HasVisibleLiquid(gridManager.GetTile(x, y)))
                        {
                            RenderCell(x, y);
                        }
                    }
                }
            }

            if (endX < gridManager.width) yield return null;
        }

        LastProcessedCellCount = gridManager.width * gridManager.height;
    }

    private void LateUpdate()
    {
        if (gridManager == null || !TryGetLiquidTilemap())
        {
            return;
        }

        if (dirtyCellQueue.Count == 0
            && dirtyDepthColumnQueue.Count == 0)
        {
            return;
        }

        if (Time.unscaledTime < nextVisualRefreshTime)
        {
            return;
        }

        nextVisualRefreshTime = Time.unscaledTime + visualRefreshInterval;
        ProcessDirtyCells();
    }

    private void HandleLiquidChanged(int x, int y)
    {
        MarkCellDirty(x, y);
        MarkDepthColumnDirty(x);
    }

    private void HandleTerrainChanged(int x, int y, TileType newType)
    {
        MarkCellDirty(x, y);
        MarkDepthColumnDirty(x);
    }

    private void MarkCellDirty(int x, int y)
    {
        if (!IsInsideGrid(x, y))
        {
            return;
        }

        Vector2Int position = new Vector2Int(x, y);
        if (dirtyCellSet.Add(position))
        {
            dirtyCellQueue.Enqueue(position);
        }
    }

    private void MarkDepthColumnDirty(int x)
    {
        if (gridManager == null || x < 0 || x >= gridManager.width)
        {
            return;
        }

        if (dirtyDepthColumnSet.Add(x))
        {
            dirtyDepthColumnQueue.Enqueue(x);
        }
    }

    private void ProcessDirtyCells()
    {
        long startedAt = PerformanceMetricsService.BeginSample();
        LastProcessedCellCount = 0;

        using (ProcessDirtyCellsMarker.Auto())
        {
            EnsureDepthCache();
            ProcessDirtyDepthColumns();

            int budget = Mathf.Max(1, maximumDirtyCellsPerRefresh);
            while (budget > 0 && dirtyCellQueue.Count > 0)
            {
                Vector2Int position = dirtyCellQueue.Dequeue();
                dirtyCellSet.Remove(position);
                RenderCell(position.x, position.y);
                LastProcessedCellCount++;
                budget--;
            }
        }

        PerformanceMetricsService.RecordLiquidVisualizerWork(
            startedAt,
            LastProcessedCellCount,
            PendingDirtyCellCount,
            PendingDepthColumnCount);
    }

    private void ProcessDirtyDepthColumns()
    {
        int budget = Mathf.Max(1, maximumDepthColumnsPerRefresh);

        using (DepthCalculationMarker.Auto())
        {
            while (budget > 0 && dirtyDepthColumnQueue.Count > 0)
            {
                int x = dirtyDepthColumnQueue.Dequeue();
                dirtyDepthColumnSet.Remove(x);
                RecalculateDepthColumn(x, true);
                budget--;
            }
        }
    }

    private void RecalculateDepthColumn(int x, bool markChangedCells)
    {
        int continuousWaterAbove = 0;

        for (int y = gridManager.height - 1; y >= 0; y--)
        {
            Tile tile = gridManager.GetTile(x, y);
            bool containsWater = HasVisibleLiquid(tile);
            int newDepth = containsWater ? continuousWaterAbove : 0;

            if (depthCache[x, y] != newDepth)
            {
                depthCache[x, y] = newDepth;
                if (markChangedCells)
                {
                    MarkCellDirty(x, y);
                }
            }

            continuousWaterAbove = containsWater
                ? continuousWaterAbove + 1
                : 0;
        }
    }

    private void RenderCell(int x, int y)
    {
        if (!IsInsideGrid(x, y))
        {
            return;
        }

        Tile tile = gridManager.GetTile(x, y);
        Vector3Int tilePosition = new Vector3Int(x, y, 0);

        if (!HasVisibleLiquid(tile))
        {
            if (liquidTilemap.HasTile(tilePosition))
            {
                liquidTilemap.SetTile(tilePosition, null);
            }
            return;
        }

        TileBase selectedTile = GetWaterTile(tile.liquidAmount);
        if (liquidTilemap.GetTile(tilePosition) != selectedTile)
        {
            liquidTilemap.SetTile(tilePosition, selectedTile);
        }

        float depthFactor = Mathf.Clamp01(
            (float)depthCache[x, y] / Mathf.Max(1, maxDepthForGradient));
        Color dynamicColor = Color.Lerp(
            surfaceWaterColor,
            deepWaterColor,
            depthFactor);

        liquidTilemap.SetTileFlags(tilePosition, TileFlags.None);
        liquidTilemap.SetColor(tilePosition, dynamicColor);
    }

    [ContextMenu("Full Refresh")]
    public void FullRefresh()
    {
        if (gridManager == null
            || !gridManager.IsGridReady
            || !TryGetLiquidTilemap())
        {
            return;
        }

        long startedAt = PerformanceMetricsService.BeginSample();
        int processedCells = gridManager.width * gridManager.height;

        using (FullRefreshMarker.Auto())
        {
            ClearDirtyBuffers();
            EnsureDepthCache(true);
            liquidTilemap.ClearAllTiles();

            using (DepthCalculationMarker.Auto())
            {
                for (int x = 0; x < gridManager.width; x++)
                {
                    RecalculateDepthColumn(x, false);
                }
            }

            for (int x = 0; x < gridManager.width; x++)
            {
                for (int y = 0; y < gridManager.height; y++)
                {
                    if (!HasVisibleLiquid(gridManager.GetTile(x, y)))
                    {
                        continue;
                    }

                    RenderCell(x, y);
                }
            }
        }

        LastProcessedCellCount = processedCells;
        PerformanceMetricsService.RecordLiquidVisualizerWork(
            startedAt,
            processedCells,
            0,
            0);
    }

    private void EnsureDepthCache(bool forceRecreate = false)
    {
        if (!forceRecreate
            && depthCache != null
            && depthCache.GetLength(0) == gridManager.width
            && depthCache.GetLength(1) == gridManager.height)
        {
            return;
        }

        depthCache = new int[gridManager.width, gridManager.height];
    }

    private void ClearDirtyBuffers()
    {
        dirtyCellQueue.Clear();
        dirtyCellSet.Clear();
        dirtyDepthColumnQueue.Clear();
        dirtyDepthColumnSet.Clear();
    }

    private bool IsInsideGrid(int x, int y)
    {
        return gridManager != null
            && x >= 0
            && x < gridManager.width
            && y >= 0
            && y < gridManager.height;
    }

    private static bool HasVisibleLiquid(Tile tile)
    {
        return tile != null && tile.liquidAmount > tile.minLiquid;
    }

    private TileBase GetWaterTile(float amount)
    {
        if (amount < 0.34f)
        {
            return waterLowTile != null ? waterLowTile : waterFullTile;
        }

        if (amount < 0.67f)
        {
            return waterMidTile != null ? waterMidTile : waterFullTile;
        }

        return waterFullTile;
    }

    private bool TryGetLiquidTilemap()
    {
        if (liquidTilemap != null)
        {
            return true;
        }

        liquidTilemap = GetComponent<Tilemap>();
        if (liquidTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning(
                "[LiquidVisualizer] Liquid Tilemap ausente ou destruído; "
                + "atualização de líquidos ignorada.",
                this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
