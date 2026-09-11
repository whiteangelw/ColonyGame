using UnityEngine;
using Unity.Profiling;

public class LiquidManager : Singleton<LiquidManager>
{
    private static readonly ProfilerMarker SimulationMarker =
        new ProfilerMarker("LiquidManager.SimulateLiquids");
    private static readonly ProfilerMarker CopyStateMarker =
        new ProfilerMarker("LiquidManager.CopyState");
    private static readonly ProfilerMarker FlowMarker =
        new ProfilerMarker("LiquidManager.CalculateFlow");
    private static readonly ProfilerMarker ApplyStateMarker =
        new ProfilerMarker("LiquidManager.ApplyState");
    private static readonly ProfilerMarker BufferResizeMarker =
        new ProfilerMarker("LiquidManager.ResizeBuffer");

    [Header("Configurações do Liquido")]
    [SerializeField] private float maxTileCapacity = 1.0f; // Máximo que um bloco suporta
    [SerializeField] private float flowSpeed = 0.25f;      // Velocidade do fluxo por tick
    [SerializeField] private float tickRate = 0.05f;       // Frequência de atualização (em segundos)

    private GridManager gridManager;
    private float timer = 0f;
    private float[,] nextLiquidState;
    private int bufferWidth;
    private int bufferHeight;

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
    }

    private void Update()
    {
        if (gridManager == null) return;

        timer += Time.deltaTime;
        if (timer >= tickRate)
        {
            timer = 0f;
            SimulateLiquids();
        }
    }

    private void SimulateLiquids()
    {
        int width = gridManager.width;
        int height = gridManager.height;
        long startedAt = PerformanceMetricsService.BeginSample();

        using (SimulationMarker.Auto())
        {
            EnsureSimulationBuffer(width, height);

            // O buffer preserva a independência entre leitura e escrita sem
            // criar uma nova matriz a cada tick.
            using (CopyStateMarker.Auto())
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Tile tile = gridManager.GetTile(x, y);
                        nextLiquidState[x, y] = tile != null
                            ? tile.liquidAmount
                            : 0f;
                    }
                }
            }

            using (FlowMarker.Auto())
            {
                CalculateFlow(width, height);
            }

            using (ApplyStateMarker.Auto())
            {
                ApplySimulationState(width, height);
            }
        }

        PerformanceMetricsService.RecordLiquidSimulationWork(
            startedAt,
            width * height);
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

    private void CalculateFlow(int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
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

    private void ApplySimulationState(int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile t = gridManager.GetTile(x, y);
                if (t != null
                    && t.liquidAmount != nextLiquidState[x, y])
                {
                    gridManager.SetLiquidAmount(
                        x,
                        y,
                        nextLiquidState[x, y]);
                }
            }
        }
    }

    // Função utilitária para adicionar água (ex: chuva ou vazamentos)
    public void AddLiquid(int x, int y, float amount)
    {
        if (gridManager == null)
        {
            return;
        }

        Tile t = gridManager.GetTile(x, y);
        if (t != null && t.type == TileType.Empty)
        {
            gridManager.SetLiquidAmount(
                x,
                y,
                Mathf.Clamp(t.liquidAmount + amount, 0f, maxTileCapacity));
        }
    }
}
