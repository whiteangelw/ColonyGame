using UnityEngine;

public static class BuildingCosts
{
    public static ResourceType GetRequiredResource(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Chest: return ResourceType.Copper;
            case TileType.PrintingPod: return ResourceType.Copper;
            case TileType.Solid: return ResourceType.Dirt;
            case TileType.Grass: return ResourceType.Dirt;
            case TileType.Stone: return ResourceType.Stone;
            case TileType.Copper: return ResourceType.Copper;
            case TileType.Iron: return ResourceType.Iron;
            case TileType.Gold: return ResourceType.Gold;
            case TileType.Ladder: return ResourceType.Dirt;
            default: return ResourceType.Dirt;
        }
    }

    public static int GetCost(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Chest: return 5;
            case TileType.PrintingPod: return 1;
            case TileType.Ladder: return 5;
            case TileType.Solid: return 1;
            case TileType.Grass: return 1;
            case TileType.Stone: return 2;
            case TileType.Copper: return 5;
            case TileType.Iron: return 3;
            case TileType.Gold: return 5;
            default: return 1;
        }
    }

    /// <summary>
    /// Percentual devolvido ao desmontar cada estrutura.
    /// Use valores entre 0 e 1: 0.5f representa 50%.
    /// </summary>
    public static float GetRefundRate(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Chest: return 1f;
            case TileType.PrintingPod: return 1f;
            case TileType.Ladder: return 1f;
            default: return 1f;
        }
    }

    public static int GetRefundAmount(TileType tileType)
    {
        return Mathf.FloorToInt(
            GetCost(tileType) * Mathf.Clamp01(GetRefundRate(tileType))
        );
    }

    public static BuildCategory GetCategory(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Chest:
                return BuildCategory.Storage;
            case TileType.PrintingPod:
                return BuildCategory.Machines;
            case TileType.Solid:
            case TileType.Grass:
            case TileType.Stone:
            case TileType.Copper:
            case TileType.Iron:
            case TileType.Gold:
            case TileType.Ladder:
                return BuildCategory.Blocks;
            default:
                return BuildCategory.Blocks;
        }
    }
}
