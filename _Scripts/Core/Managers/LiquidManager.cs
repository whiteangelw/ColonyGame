using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class LiquidManager : Singleton<LiquidManager>
{
    private static readonly ProfilerMarker SimulationMarker =
        new ProfilerMarker("LiquidManager.SimulateActiveRegions");
    private static readonly ProfilerMarker CopyStateMarker =
        new ProfilerMarker("LiquidManager.CopyRegionState");
    private static readonly ProfilerMarker FlowMarker =
        new ProfilerMarker("LiquidManager.CalculateRegionFlow");
    private static readonly ProfilerMarker ApplyStateMarker =
        new ProfilerMarker("LiquidManager.ApplyRegionState");
    private static readonly ProfilerMarker BufferResizeMarker =
        new ProfilerMarker("LiquidManager.ResizeBuffer");

    [Header("Configurações do Líquido")]
    [SerializeField, Min(0.01f)] private float maxTileCapacity = 1f;
    [SerializeField, Min(0.001f)] private float flowSpeed = 0.25f;
    [SerializeField, Min(0.01f)] private float tickRate = 0.05f;

    [Header("P5E - Simulação regional")]
    [Tooltip("Quantidade máxima de regiões líquidas simuladas por tick.")]
    [SerializeField, Min(1)] private int maximumActiveRegionsPerTick = 4;

    private readonly Queue<WorldRegionCoordinate> activeRegionQueue =
        new Queue<WorldRegionCoordinate>(32);

    private readonly HashSet<WorldRegionCoordinate> activeRegionSet =
        new HashSet<WorldRegionCoordinate>();

    private readonly List<WorldRegionState> simulationBatch =
        new List<WorldRegionState>(8);

    private GridManager gridManager;
    private float timer;
    private float[,] nextLiquidState;
    private int bufferWidth;
    private int bufferHeight;

    public int PendingActiveRegionCount => activeRegionSet.Count;

    protected override void Awake()
    {
        base.Awake();

        gridManager = GridManager.Instance != null
            ? GridManager.Instance
            : FindFirstObjectByType<GridManager>();
    }

    private void Start()
    {
        if (gridManager == null)
        {
            gridManager = GridManager.Instance != null
                ? GridManager.Instance
                : FindFirstObjectByType<GridManager>();
        }

        if (gridManager == null)
        {
            return;
        }

        gridManager.OnGridRebuilt += HandleGridRebuilt;
        gridManager.OnLiquidChanged += HandleLiquidChanged;
        gridManager.OnTileChanged += HandleTerrainChanged;

        if (gridManager.IsGridReady)
        {
            RebuildActiveRegionsFromGrid();
        }
    }

    private void OnDestroy()
    {
        if (gridManager == null)
        {
            return;
        }

        gridManager.OnGridRebuilt -= HandleGridRebuilt;
        gridManager.OnLiquidChanged -= HandleLiquidChanged;
        gridManager.OnTileChanged -= HandleTerrainChanged;
    }

    private void Update()
    {
        if (gridManager == null || !gridManager.IsGridReady)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer < tickRate)
        {
            return;
        }

        timer -= tickRate;

        if (activeRegionQueue.Count > 0)
        {
            SimulateActiveRegions();
        }
    }

    private void HandleGridRebuilt()
    {
        RebuildActiveRegionsFromGrid();
    }

    private void HandleLiquidChanged(int x, int y)
    {
        ActivateCellAndNeighborRegions(x, y);
    }

    private void HandleTerrainChanged(int x, int y, TileType newType)
    {
        ActivateCellAndNeighborRegions(x, y);
    }

    private void RebuildActiveRegionsFromGrid()
    {
        ClearActiveRegions();

        if (gridManager == null || !gridManager.IsGridReady)
        {
            return;
        }

        EnsureSimulationBuffer(gridManager.width, gridManager.height);

        for (int x = 0; x < gridManager.width; x++)
        {
            for (int y = 0; y < gridManager.height; y++)
            {
                Tile tile = gridManager.GetTile(x, y);

                if (tile != null && tile.liquidAmount > tile.minLiquid)
                {
                    ActivateRegionAtCell(x, y);
                }
            }
        }
    }

    private void SimulateActiveRegions()
    {
        EnsureSimulationBuffer(gridManager.width, gridManager.height);
        simulationBatch.Clear();

        int regionBudget = Mathf.Max(1, maximumActiveRegionsPerTick);

        while (simulationBatch.Count < regionBudget
               && activeRegionQueue.Count > 0)
        {
            WorldRegionCoordinate coordinate = activeRegionQueue.Dequeue();
            activeRegionSet.Remove(coordinate);

            if (gridManager.Regions.TryGet(
                    coordinate,
                    out WorldRegionState region))
            {
                simulationBatch.Add(region);
            }
        }

        if (simulationBatch.Count == 0)
        {
            return;
        }

        long startedAt = PerformanceMetricsService.BeginSample();
        int processedCells = 0;

        using (SimulationMarker.Auto())
        {
            using (CopyStateMarker.Auto())
            {
                foreach (WorldRegionState region in simulationBatch)
                {
                    CopyRegionState(region.Bounds, 1);
                }
            }

            using (FlowMarker.Auto())
            {
                foreach (WorldRegionState region in simulationBatch)
                {
                    CalculateFlow(region.Bounds);
                    processedCells += GetCellCount(region.Bounds);
                }
            }

            using (ApplyStateMarker.Auto())
            {
                foreach (WorldRegionState region in simulationBatch)
                {
                    ApplySimulationState(region.Bounds, 1);
                }
            }
        }

        PerformanceMetricsService.RecordLiquidSimulationWork(
            startedAt,
            processedCells);
    }

    private void EnsureSimulationBuffer(int width, int height)
    {
        if (nextLiquidState != null
            && bufferWidth == width
            && bufferHeight == height)
        {
            return;
        }

        using (BufferResizeMarker.Auto())
        {
            nextLiquidState = new float[width, height];
            bufferWidth = width;
            bufferHeight = height;
        }

        PerformanceMetricsService.RecordLiquidBufferResize();
    }

    private void CopyRegionState(WorldRegionBounds bounds, int padding)
    {
        GetPaddedBounds(
            bounds,
            padding,
            out int minimumX,
            out int minimumY,
            out int maximumX,
            out int maximumY);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                Tile tile = gridManager.GetTile(x, y);

                nextLiquidState[x, y] = tile != null
                    ? tile.liquidAmount
                    : 0f;
            }
        }
    }

    private void CalculateFlow(WorldRegionBounds bounds)
    {
        for (int x = bounds.MinimumX; x <= bounds.MaximumX; x++)
        {
            for (int y = bounds.MinimumY; y <= bounds.MaximumY; y++)
            {
                Tile currentTile = gridManager.GetTile(x, y);

                if (currentTile == null || currentTile.type != TileType.Empty)
                {
                    continue;
                }

                float currentAmount = nextLiquidState[x, y];

                if (currentAmount <= currentTile.minLiquid)
                {
                    continue;
                }

                Tile downTile = gridManager.GetTile(x, y - 1);

                if (downTile != null
                    && downTile.type == TileType.Empty
                    && downTile.liquidAmount < maxTileCapacity)
                {
                    float spaceInDown =
                        maxTileCapacity - nextLiquidState[x, y - 1];

                    float flow = Mathf.Min(
                        currentAmount,
                        spaceInDown,
                        flowSpeed);

                    nextLiquidState[x, y] -= flow;
                    nextLiquidState[x, y - 1] += flow;
                    currentAmount -= flow;
                }

                if (currentAmount <= currentTile.minLiquid)
                {
                    continue;
                }

                Tile leftTile = gridManager.GetTile(x - 1, y);
                Tile rightTile = gridManager.GetTile(x + 1, y);

                bool canLeft = leftTile != null
                    && leftTile.type == TileType.Empty;

                bool canRight = rightTile != null
                    && rightTile.type == TileType.Empty;

                if (!canLeft && !canRight)
                {
                    continue;
                }

                float leftAmount = canLeft
                    ? nextLiquidState[x - 1, y]
                    : maxTileCapacity;

                float rightAmount = canRight
                    ? nextLiquidState[x + 1, y]
                    : maxTileCapacity;

                if (canLeft && leftAmount < currentAmount)
                {
                    float flow = Mathf.Min(
                        (currentAmount - leftAmount) * 0.5f,
                        flowSpeed);

                    nextLiquidState[x, y] -= flow;
                    nextLiquidState[x - 1, y] += flow;
                    currentAmount -= flow;
                }

                if (canRight && rightAmount < currentAmount)
                {
                    float flow = Mathf.Min(
                        (currentAmount - rightAmount) * 0.5f,
                        flowSpeed);

                    nextLiquidState[x, y] -= flow;
                    nextLiquidState[x + 1, y] += flow;
                }
            }
        }
    }

    private void ApplySimulationState(WorldRegionBounds bounds, int padding)
    {
        GetPaddedBounds(
            bounds,
            padding,
            out int minimumX,
            out int minimumY,
            out int maximumX,
            out int maximumY);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                Tile tile = gridManager.GetTile(x, y);

                if (tile != null
                    && tile.liquidAmount != nextLiquidState[x, y])
                {
                    gridManager.SetLiquidAmount(
                        x,
                        y,
                        nextLiquidState[x, y]);
                }
            }
        }
    }

    private void ActivateCellAndNeighborRegions(int x, int y)
    {
        ActivateRegionAtCell(x, y);
        ActivateRegionAtCell(x - 1, y);
        ActivateRegionAtCell(x + 1, y);
        ActivateRegionAtCell(x, y - 1);
        ActivateRegionAtCell(x, y + 1);
    }

    private void ActivateRegionAtCell(int x, int y)
    {
        if (gridManager == null
            || !gridManager.Regions.TryGetByCell(
                x,
                y,
                out WorldRegionState region))
        {
            return;
        }

        WorldRegionCoordinate coordinate = region.Coordinate;

        if (activeRegionSet.Add(coordinate))
        {
            activeRegionQueue.Enqueue(coordinate);
        }
    }

    private void ClearActiveRegions()
    {
        activeRegionQueue.Clear();
        activeRegionSet.Clear();
        simulationBatch.Clear();
    }

    private void GetPaddedBounds(
        WorldRegionBounds bounds,
        int padding,
        out int minimumX,
        out int minimumY,
        out int maximumX,
        out int maximumY)
    {
        minimumX = Mathf.Max(0, bounds.MinimumX - padding);
        minimumY = Mathf.Max(0, bounds.MinimumY - padding);

        maximumX = Mathf.Min(
            gridManager.width - 1,
            bounds.MaximumX + padding);

        maximumY = Mathf.Min(
            gridManager.height - 1,
            bounds.MaximumY + padding);
    }

    private static int GetCellCount(WorldRegionBounds bounds)
    {
        return (bounds.MaximumX - bounds.MinimumX + 1)
            * (bounds.MaximumY - bounds.MinimumY + 1);
    }

    // Usado por chuva, vazamentos e ferramentas de desenvolvimento.
    public void AddLiquid(int x, int y, float amount)
    {
        if (gridManager == null)
        {
            return;
        }

        Tile tile = gridManager.GetTile(x, y);

        if (tile != null && tile.type == TileType.Empty)
        {
            gridManager.SetLiquidAmount(
                x,
                y,
                Mathf.Clamp(
                    tile.liquidAmount + amount,
                    0f,
                    maxTileCapacity));
        }
    }
}