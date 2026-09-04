using System;
using UnityEngine;

public class GridManager : Singleton<GridManager>, IGridService
{
    [Header("Configurações do Mapa")]
    public int width = 20;
    public int height = 15;
    public float cellSize = 1.0f;

    public bool IsGridReady { get; private set; }

    public int Width => width;
    public int Height => height;

    public event Action<int, int, TileType> OnTileChanged;
    public event Action OnGridRebuilt;

    private Tile[,] grid;

    public IStorage GetAvailableStorageFor(ResourceType type, int amount)
    {
        return StructureManager.Instance?.GetAvailableStorageFor(type, amount);
    }

    private void Start()
    {
        GenerateGrid();
    }

    private void GenerateGrid()
    {
        IsGridReady = false;

        grid = new Tile[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Tile(x, y, TileType.Empty);
            }
        }

        WorldGenerator generator = GetComponent<WorldGenerator>();

        if (generator != null)
        {
            generator.GenerateWorld();
        }

        // Mantém as bordas do mapa protegidas.
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || x == width - 1 || y == 0)
                {
                    SetTileType(x, y, TileType.Bedrock);
                }
            }
        }

        IsGridReady = true;
        OnGridRebuilt?.Invoke();
    }

    public Tile GetTile(int x, int y)
    {
        if (!IsInsideGrid(x, y))
        {
            return null;
        }

        return grid[x, y];
    }

    public Tile GetTile(Vector2Int gridPos)
    {
        return GetTile(gridPos.x, gridPos.y);
    }

    public TileType GetTileType(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null
            ? tile.type
            : TileType.Empty;
    }

    public void SetTileType(int x, int y, TileType newType)
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return;
        }

        bool createsStructure = newType == TileType.Chest
            || newType == TileType.PrintingPod;

        if (tile.type == newType && !createsStructure)
        {
            return;
        }

        bool isSolid = IsSolidTileType(newType);

        // Evita que entidades permaneçam dentro de um bloco sólido.
        if (isSolid)
        {
            GridSafetyUtility.EjectEntitiesFromTile(x, y);
        }

        tile.type = newType;
        tile.isPassable = !isSolid;

        if (createsStructure)
        {
            StructureManager.Instance?.SpawnStructure(
                new Vector2Int(x, y),
                newType
            );
        }

        OnTileChanged?.Invoke(x, y, newType);
    }

    public Vector2Int WorldToGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / cellSize);
        int y = Mathf.FloorToInt(worldPosition.y / cellSize);

        return new Vector2Int(x, y);
    }

    public Vector3 GridToWorldPosition(Vector2Int gridPosition)
    {
        return new Vector3(
            gridPosition.x * cellSize + cellSize * 0.5f,
            gridPosition.y * cellSize + cellSize * 0.5f,
            0f
        );
    }

    public StorageStructure GetChestAt(Vector2Int gridPos)
    {
        return StructureManager.Instance?.GetStorageAt(gridPos);
    }

    /// <summary>
    /// Retorna true quando o tile existe e pode ser ocupado.
    /// </summary>
    public bool IsPassable(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && tile.isPassable;
    }

    public bool IsPassable(Vector2Int position)
    {
        return IsPassable(position.x, position.y);
    }

    /// <summary>
    /// Retorna true quando o tile é sólido.
    /// Fora do mapa também é considerado sólido.
    /// </summary>
    public bool IsSolid(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && !tile.isPassable;
    }

    public bool IsSolid(Vector2Int position)
    {
        return IsSolid(position.x, position.y);
    }

    public bool IsLadder(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && tile.type == TileType.Ladder;
    }

    public bool IsLadder(Vector2Int position)
    {
        return IsLadder(position.x, position.y);
    }

    public bool IsEmptySpace(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && tile.type == TileType.Empty;
    }

    public bool IsEmptySpace(Vector2Int position)
    {
        return IsEmptySpace(position.x, position.y);
    }

    /// <summary>
    /// Verifica se existe alguma forma de suporte físico abaixo do tile.
    /// </summary>
    public bool HasSupportBelow(int x, int y)
    {
        return IsSolid(x, y - 1) || IsLadder(x, y - 1);
    }

    public bool HasSupportBelow(Vector2Int position)
    {
        return HasSupportBelow(position.x, position.y);
    }

    /// <summary>
    /// Verifica se uma entidade de dois tiles de altura
    /// pode ocupar esta posição.
    /// </summary>
    public bool IsStandable(int x, int y)
    {
        if (!IsPassable(x, y))
        {
            return false;
        }

        // O segundo tile representa a parte superior do corpo.
        if (!IsPassable(x, y + 1))
        {
            return false;
        }

        return HasSupportBelow(x, y);
    }

    public bool IsStandable(Vector2Int position)
    {
        return IsStandable(position.x, position.y);
    }

    /// <summary>
    /// Verifica apenas se o espaço é válido para o corpo do colono.
    /// Não exige suporte.
    /// </summary>
    public bool IsOccupiable(int x, int y)
    {
        return IsPassable(x, y)
            && IsPassable(x, y + 1);
    }

    public bool IsOccupiable(Vector2Int position)
    {
        return IsOccupiable(position.x, position.y);
    }

    private bool IsInsideGrid(int x, int y)
    {
        return x >= 0
            && x < width
            && y >= 0
            && y < height;
    }

    private bool IsSolidTileType(TileType type)
    {
        return type != TileType.Empty
            && type != TileType.Ladder
            && type != TileType.Chest
            && type != TileType.PrintingPod;
    }
}
