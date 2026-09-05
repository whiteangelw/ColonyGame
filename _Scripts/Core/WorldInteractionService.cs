using UnityEngine;

public class WorldInteractionService : Singleton<WorldInteractionService>
{
    public void DigTile(int x, int y)
    {
        if (GridManager.Instance == null) return;

        Tile tile = GridManager.Instance.GetTile(x, y);
        if (tile == null || tile.type == TileType.Empty || tile.type == TileType.Bedrock) return;
        if (tile.type == TileType.Chest
            || tile.type == TileType.Ladder
            || tile.type == TileType.PrintingPod) return;

        ResourceType? droppedResource = GetResourceTypeFromTile(tile.type);

        GridManager.Instance.SetTileType(x, y, TileType.Empty);

        if (droppedResource.HasValue)
        {
            float cellSize = GridManager.Instance.cellSize;
            Vector3 spawnPos = new Vector3(x * cellSize + cellSize / 2f, y * cellSize + cellSize / 2f, 0);

            ItemSpawner.Instance?.SpawnResource(droppedResource.Value, spawnPos);
            StockpileManager.Instance?.RefreshReachableResources();
            GameEvents.TriggerFloatingTextRequested($"+1 {droppedResource.Value}", spawnPos, Color.green);
        }
    }

    /// <summary>
    /// Registra uma tarefa para o colono ir até o local e desmontar a estrutura.
    /// </summary>
    public void DismantleTile(int x, int y)
    {
        if (GridManager.Instance == null) return;

        Vector2Int position = new Vector2Int(x, y);
        UnityEngine.Object occupant = GridManager.Instance.GetOccupantAt(position);
        StorageStructure occupiedStorage = occupant as StorageStructure;
        PrintingPod occupiedPrintingPod = occupant as PrintingPod;

        if (occupiedStorage != null)
        {
            position = occupiedStorage.GridPosition;
        }
        else if (occupiedPrintingPod != null)
        {
            position = occupiedPrintingPod.GridPosition;
        }

        Tile tile = GridManager.Instance.GetTile(x, y);
        bool hasWorldStructure = occupiedStorage != null
            || occupiedPrintingPod != null;

        if (tile == null || (tile.type == TileType.Empty && !hasWorldStructure))
        {
            return;
        }

        if (hasWorldStructure
            || tile.type == TileType.Chest
            || tile.type == TileType.Ladder
            || tile.type == TileType.PrintingPod)
        {
            if (TaskManager.Instance == null
                || TaskManager.Instance.GetTaskAt(position) != null)
            {
                return;
            }

            if (occupiedStorage != null || tile.type == TileType.Chest)
            {
                StorageStructure storage =
                    StructureManager.Instance?.GetStorageAt(position);

                if (storage == null || !storage.MarkAsDismantled())
                {
                    return;
                }
            }

            TaskManager.Instance.AddTask(position, TaskType.Dismantle);
        }
    }

    /// <summary>
    /// Executado pelo Colono via DismantleTaskHandler quando ele chega no local.
    /// </summary>
    public void ExecuteDismantleAt(int x, int y)
    {
        if (GridManager.Instance == null) return;

        Vector2Int position = new Vector2Int(x, y);
        UnityEngine.Object occupant = GridManager.Instance.GetOccupantAt(position);

        if (occupant is StorageStructure storage)
        {
            StructureManager.Instance?.DismantleStructureAt(
                storage.GridPosition);
            return;
        }

        if (occupant is PrintingPod printingPod)
        {
            StructureManager.Instance?.DismantleStructureAt(
                printingPod.GridPosition);
            return;
        }

        Tile tile = GridManager.Instance.GetTile(x, y);
        if (tile == null) return;

        if (tile.type == TileType.Chest)
        {
            StructureManager.Instance?.DismantleStructureAt(new Vector2Int(x, y));
        }
        else if (tile.type == TileType.PrintingPod)
        {
            StructureManager.Instance?.DismantleStructureAt(
                new Vector2Int(x, y)
            );
        }
        else if (tile.type == TileType.Ladder)
        {
            float cellSize = GridManager.Instance.cellSize;
            Vector3 spawnPos = new Vector3(x * cellSize + cellSize / 2f, y * cellSize + cellSize / 2f, 0);

            GridManager.Instance.SetTileType(x, y, TileType.Empty);

            ResourceType reqResource = BuildingCosts.GetRequiredResource(TileType.Ladder);
            int refundAmount = BuildingCosts.GetRefundAmount(TileType.Ladder);

            ItemSpawner.Instance?.SpawnResource(reqResource, spawnPos, refundAmount);
            StockpileManager.Instance?.RefreshReachableResources();
            GameEvents.TriggerFloatingTextRequested("Estrutura Desmontada", spawnPos, Color.yellow);
        }
    }

    private ResourceType? GetResourceTypeFromTile(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Solid:
            case TileType.Grass: return ResourceType.Dirt;
            case TileType.Stone: return ResourceType.Stone;
            case TileType.Copper: return ResourceType.Copper;
            case TileType.Coal: return ResourceType.Coal;
            case TileType.Iron: return ResourceType.Iron;
            case TileType.Gold: return ResourceType.Gold;
            default: return null;
        }
    }
}
