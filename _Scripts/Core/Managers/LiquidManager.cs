using UnityEngine;

public class LiquidManager : Singleton<LiquidManager>
{

    [Header("Configurações do Liquido")]
    [SerializeField] private float maxTileCapacity = 1.0f; // Máximo que um bloco suporta
    [SerializeField] private float flowSpeed = 0.25f;      // Velocidade do fluxo por tick
    [SerializeField] private float tickRate = 0.05f;       // Frequência de atualização (em segundos)

    private GridManager gridManager;
    private float timer = 0f;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
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

        // Criamos uma matriz temporária para evitar que a ordem de varredura afete o fluxo
        float[,] nextLiquidState = new float[width, height];

        // Copia o estado atual
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile t = gridManager.GetTile(x, y);
                nextLiquidState[x, y] = (t != null) ? t.liquidAmount : 0f;
            }
        }

        // Executa a física de fluidos
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile currentTile = gridManager.GetTile(x, y);
                if (currentTile == null || currentTile.type != TileType.Empty) continue;

                float currentAmount = nextLiquidState[x, y];
                if (currentAmount <= currentTile.minLiquid) continue;

                // 1. TENTA MOVER PARA BAIXO
                Tile downTile = gridManager.GetTile(x, y - 1);
                if (downTile != null && downTile.type == TileType.Empty && downTile.liquidAmount < maxTileCapacity)
                {
                    float spaceInDown = maxTileCapacity - nextLiquidState[x, y - 1];
                    float flow = Mathf.Min(currentAmount, spaceInDown, flowSpeed);

                    nextLiquidState[x, y] -= flow;
                    nextLiquidState[x, y - 1] += flow;
                    currentAmount -= flow;
                }

                if (currentAmount <= currentTile.minLiquid) continue;

                // 2. TENTA MOVER PARA OS LADOS (ESQUERDA E DIREITA)
                Tile leftTile = gridManager.GetTile(x - 1, y);
                Tile rightTile = gridManager.GetTile(x + 1, y);

                bool canLeft = leftTile != null && leftTile.type == TileType.Empty;
                bool canRight = rightTile != null && rightTile.type == TileType.Empty;

                if (canLeft || canRight)
                {
                    float leftAmount = canLeft ? nextLiquidState[x - 1, y] : maxTileCapacity;
                    float rightAmount = canRight ? nextLiquidState[x + 1, y] : maxTileCapacity;

                    // Calcula nivelamento de água nos vizinhos
                    float totalAmount = currentAmount;
                    int count = 1;

                    if (canLeft && leftAmount < currentAmount) { totalAmount += leftAmount; count++; }
                    if (canRight && rightAmount < currentAmount) { totalAmount += rightAmount; count++; }

                    float average = totalAmount / count;

                    if (canLeft && leftAmount < currentAmount)
                    {
                        float flow = Mathf.Min((currentAmount - leftAmount) * 0.5f, flowSpeed);
                        nextLiquidState[x, y] -= flow;
                        nextLiquidState[x - 1, y] += flow;
                        currentAmount -= flow;
                    }

                    if (canRight && rightAmount < currentAmount)
                    {
                        float flow = Mathf.Min((currentAmount - rightAmount) * 0.5f, flowSpeed);
                        nextLiquidState[x, y] -= flow;
                        nextLiquidState[x + 1, y] += flow;
                    }
                }
            }
        }

        // Aplica o novo estado à grade
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile t = gridManager.GetTile(x, y);
                if (t != null)
                {
                    t.liquidAmount = nextLiquidState[x, y];
                }
            }
        }
    }

    // Função utilitária para adicionar água (ex: chuva ou vazamentos)
    public void AddLiquid(int x, int y, float amount)
    {
        Tile t = gridManager.GetTile(x, y);
        if (t != null && t.type == TileType.Empty)
        {
            t.liquidAmount = Mathf.Clamp(t.liquidAmount + amount, 0f, maxTileCapacity);
        }
    }
}