using System;

/// <summary>
/// Armazena os dados brutos do grid. Não executa regras de gameplay,
/// não instancia estruturas e não dispara eventos.
/// </summary>
public sealed class GridData
{
    private Tile[,] tiles;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsCreated => tiles != null;

    public void Create(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        tiles = new Tile[Width, Height];

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                tiles[x, y] = new Tile(x, y, TileType.Empty);
            }
        }
    }

    public bool IsInside(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// Copia um resultado de geração para o estado mutável do runtime.
    /// Não publica eventos e não executa regras de gameplay.
    /// </summary>
    public bool ApplyGenerationResult(WorldGenerationResult result)
    {
        if (result == null
            || !IsCreated
            || result.Width != Width
            || result.Height != Height)
        {
            return false;
        }

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                WorldCellData generated = result.GetCell(x, y);
                Tile tile = tiles[x, y];

                tile.type = generated.TerrainType;
                tile.structureType = TileType.Empty;
                tile.backWallType = TileType.Empty;
                tile.decorationType = TileType.Empty;
                tile.terrainContentId = string.Empty;
                tile.structureContentId = string.Empty;
                tile.backWallContentId = string.Empty;
                tile.decorationContentId = string.Empty;
                tile.isPassable = IsPassableTerrain(generated.TerrainType);
                tile.fogState = generated.FogState;
                tile.liquidAmount = Math.Max(0f, generated.LiquidAmount);
                tile.reachabilityGroupID = -1;
            }
        }

        return true;
    }

    private static bool IsPassableTerrain(TileType type)
    {
        return type == TileType.Empty
            || type == TileType.Ladder
            || type == TileType.Chest
            || type == TileType.PrintingPod
            || type == TileType.Structure;
    }

    public Tile GetTile(int x, int y)
    {
        return IsCreated && IsInside(x, y)
            ? tiles[x, y]
            : null;
    }

    public TileType GetTileType(int x, int y, GridLayer layer)
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return TileType.Empty;
        }

        switch (layer)
        {
            case GridLayer.Structure:
                return tile.structureType;

            case GridLayer.BackWall:
                return tile.backWallType;

            case GridLayer.Decoration:
                return tile.decorationType;

            default:
                return tile.type;
        }
    }

    public bool SetTileType(int x, int y, GridLayer layer, TileType type)
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return false;
        }

        switch (layer)
        {
            case GridLayer.Structure:
                tile.structureType = type;
                break;

            case GridLayer.BackWall:
                tile.backWallType = type;
                break;

            case GridLayer.Decoration:
                tile.decorationType = type;
                break;

            default:
                tile.type = type;
                break;
        }

        return true;
    }

    public string GetContentId(int x, int y, GridLayer layer)
    {
        Tile tile = GetTile(x, y);
        if (tile == null) return string.Empty;

        switch (layer)
        {
            case GridLayer.BackWall:
                return tile.backWallContentId;
            case GridLayer.Structure:
                return tile.structureContentId;
            case GridLayer.Decoration:
                return tile.decorationContentId;
            default:
                return tile.terrainContentId;
        }
    }

    public bool SetContentId(
        int x,
        int y,
        GridLayer layer,
        string contentId)
    {
        Tile tile = GetTile(x, y);
        if (tile == null) return false;

        string normalizedId = contentId?.Trim() ?? string.Empty;

        switch (layer)
        {
            case GridLayer.BackWall:
                tile.backWallContentId = normalizedId;
                break;
            case GridLayer.Structure:
                tile.structureContentId = normalizedId;
                break;
            case GridLayer.Decoration:
                tile.decorationContentId = normalizedId;
                break;
            default:
                tile.terrainContentId = normalizedId;
                break;
        }

        return true;
    }
}
