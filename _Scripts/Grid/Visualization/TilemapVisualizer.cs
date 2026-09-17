using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Profiling;

public class TilemapVisualizer : MonoBehaviour
{
    private static readonly ProfilerMarker FullRefreshMarker =
        new ProfilerMarker("TilemapVisualizer.FullRefresh");
    private static readonly ProfilerMarker FullRefreshBatchMarker =
        new ProfilerMarker("TilemapVisualizer.FullRefreshBatch");
    private static readonly ProfilerMarker RegionalRefreshMarker =
        new ProfilerMarker("TilemapVisualizer.RegionalRefreshBatch");
    [System.Serializable]
    public struct TileMapping
    {
        public TileType tileType;
        public TileBase tileAsset;
    }

    [Header("Referências")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private GridLayer visualizedLayer = GridLayer.Terrain;

    [Header("Carregamento incremental")]
    [SerializeField, Min(1)] private int fullRefreshColumnsPerFrame = 32;

    [Header("P5E - Atualização regional")]
    [Tooltip("Limite de células alteradas processadas por frame neste Tilemap.")]
    [SerializeField, Min(1)] private int maximumDirtyCellsPerFrame = 2048;
    [Tooltip("Fatia máxima processada por região antes de passar para a próxima.")]
    [SerializeField, Min(1)] private int maximumDirtyCellsPerRegionPass = 256;

    [Header("Fallback visual legado")]
    [Tooltip("Usado apenas quando o conteúdo não possui BuildDefinitionSO/Tile Asset.")]
    [SerializeField] private List<TileMapping> mappingList = new List<TileMapping>();

    private Dictionary<TileType, TileBase> tileDictionary;
    private readonly Dictionary<int, Queue<int>> pendingCellsByRegion =
        new Dictionary<int, Queue<int>>();
    private readonly Queue<int> pendingRegionOrder = new Queue<int>();
    private readonly HashSet<int> pendingCellKeys = new HashSet<int>();
    private readonly Stack<Queue<int>> cellQueuePool = new Stack<Queue<int>>();
    private GridManager subscribedGridManager;
    private bool missingTilemapLogged;

    public int PendingVisualCellCount => pendingCellKeys.Count;
    public int PendingVisualRegionCount => pendingCellsByRegion.Count;

    private void Awake()
    {
        if (targetTilemap == null)
            targetTilemap = GetComponent<Tilemap>();

        InitializeDictionary();
    }

    private void Start()
    {
        subscribedGridManager = GridManager.Instance;

        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnTileChanged += OnTileChangedHandler;
            subscribedGridManager.OnLayerTileChanged += OnLayerTileChangedHandler;
            subscribedGridManager.OnGridRebuilt += HandleGridRebuilt;

            // Se o grid já estiver pronto quando o Start rodar, desenha imediatamente
            if (subscribedGridManager.IsGridReady)
            {
                RenderFullGrid();
            }
        }
    }

    private void OnDestroy()
    {
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnTileChanged -= OnTileChangedHandler;
            subscribedGridManager.OnLayerTileChanged -= OnLayerTileChangedHandler;
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
            RenderFullGrid();
        }
    }

    public IEnumerator RenderFullGridIncrementally()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !TryGetTargetTilemap()) yield break;

        ClearPendingVisualChanges();
        targetTilemap.ClearAllTiles();
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
                        TileType type = grid.GetTileType(x, y, visualizedLayer);
                        if (TryResolveTileAsset(x, y, type, out TileBase tileAsset))
                        {
                            targetTilemap.SetTile(new Vector3Int(x, y, 0), tileAsset);
                        }
                    }
                }
            }

            if (endX < grid.width) yield return null;
        }
    }

    private void InitializeDictionary()
    {
        tileDictionary = new Dictionary<TileType, TileBase>();
        foreach (var mapping in mappingList)
        {
            if (mapping.tileAsset != null && !tileDictionary.ContainsKey(mapping.tileType))
                tileDictionary.Add(mapping.tileType, mapping.tileAsset);
        }
    }

    private void OnTileChangedHandler(int x, int y, TileType newType)
    {
        if (visualizedLayer != GridLayer.Terrain && visualizedLayer != GridLayer.Structure) return;
        QueueVisualChange(x, y);
    }

    private void OnLayerTileChangedHandler(int x, int y, GridLayer layer, TileType newType)
    {
        if (layer != visualizedLayer) return;
        QueueVisualChange(x, y);
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
        if (grid == null || !grid.IsGridReady || !TryGetTargetTilemap()) return;

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
                    RenderCell(
                        x,
                        y,
                        grid.GetTileType(x, y, visualizedLayer));
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
                    // Round-robin: uma região grande não bloqueia as demais.
                    pendingRegionOrder.Enqueue(regionKey);
                }
            }
        }

        PerformanceMetricsService.RecordTilemapRegionalWork(
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

    private void RenderCell(int x, int y, TileType newType)
    {
        if (!TryGetTargetTilemap()) return;

        Vector3Int tilePosition = new Vector3Int(x, y, 0);

        if (TryResolveTileAsset(x, y, newType, out TileBase tileAsset))
            targetTilemap.SetTile(tilePosition, tileAsset);
        else
            targetTilemap.SetTile(tilePosition, null);
    }

    public void RenderFullGrid()
    {
        if (GridManager.Instance == null || !TryGetTargetTilemap()) return;

        ClearPendingVisualChanges();
        long startedAt = PerformanceMetricsService.BeginSample();
        using (FullRefreshMarker.Auto())
        {

        targetTilemap.ClearAllTiles();

        for (int x = 0; x < GridManager.Instance.width; x++)
        {
            for (int y = 0; y < GridManager.Instance.height; y++)
            {
                TileType type = GridManager.Instance.GetTileType(x, y, visualizedLayer);
                Vector3Int pos = new Vector3Int(x, y, 0);

                if (TryResolveTileAsset(x, y, type, out TileBase tileAsset))
                    targetTilemap.SetTile(pos, tileAsset);
            }
        }
        }
        PerformanceMetricsService.EndSample(
            PerformanceMetric.TilemapFullRefresh,
            startedAt);
    }

    private bool TryResolveTileAsset(
        int x,
        int y,
        TileType fallbackType,
        out TileBase tileAsset)
    {
        tileAsset = null;

        string contentId = GridManager.Instance?.GetContentId(
            x,
            y,
            visualizedLayer);
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(contentId);

        if (definition != null
            && definition.PlacementLayer == visualizedLayer)
        {
            // Prefabs físicos desenham a própria imagem. Não desenhe também
            // um Tilemap por baixo deles.
            if (definition.HasPhysicalPrefab)
            {
                return false;
            }

            if (definition.TileAsset == null)
            {
                return false;
            }

            tileAsset = definition.TileAsset;
            return true;
        }

        return tileDictionary.TryGetValue(fallbackType, out tileAsset);
    }

    private bool TryGetTargetTilemap()
    {
        if (targetTilemap != null) return true;

        targetTilemap = GetComponent<Tilemap>();
        if (targetTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning("[TilemapVisualizer] Tilemap ausente ou destruído; atualização visual ignorada.", this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
