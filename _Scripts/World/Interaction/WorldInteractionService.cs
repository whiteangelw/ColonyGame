using UnityEngine;

public class WorldInteractionService : Singleton<WorldInteractionService>
{
    public bool CanDigTile(int x, int y, out string failureReason)
    {
        failureReason = string.Empty;
        GridManager grid = GridManager.Instance;
        if (grid == null || !grid.IsInsideGrid(x, y))
        {
            failureReason = "Posição fora do grid.";
            return false;
        }

        Tile tile = grid.GetTile(x, y);
        if (tile == null || tile.type == TileType.Empty
            || tile.type == TileType.Bedrock || tile.type == TileType.Chest
            || tile.type == TileType.Ladder || tile.type == TileType.PrintingPod)
        {
            failureReason = "Este tile não pode ser escavado.";
            return false;
        }

        if (grid.IsRequiredStructureSupport(
            new Vector2Int(x, y), out GridOccupancyRecord record))
        {
            failureReason = record != null && record.IsBlueprint
                ? "Este tile sustenta um blueprint. Cancele-o primeiro."
                : "Este tile sustenta uma estrutura. Desmonte-a primeiro.";
            return false;
        }

        return true;
    }

    public void DigTile(int x, int y)
    {
        if (!CanDigTile(x, y, out _)) return;

        Tile tile = GridManager.Instance.GetTile(x, y);

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
    public bool DismantleTile(int x, int y)
    {
        Vector2Int position = new Vector2Int(x, y);

        // Compatibilidade com chamadas antigas: estruturas têm prioridade.
        GridLayer layer = GridManager.Instance != null
            && GridManager.Instance.GetOccupantAt(
                position,
                GridLayer.Structure) != null
            ? GridLayer.Structure
            : GridLayer.Terrain;

        return DismantleTile(x, y, layer);
    }

    public bool DismantleTile(int x, int y, GridLayer layer)
    {
        if (GridManager.Instance == null) return false;

        Vector2Int position = new Vector2Int(x, y);
        UnityEngine.Object occupant = GridManager.Instance.GetOccupantAt(
            position,
            layer);

        if (occupant is IDismantlable dismantlable)
        {
            position = dismantlable.GridPosition;
        }

        if (!CanDismantleAt(position, layer))
        {
            return false;
        }

        if (TaskManager.Instance == null
            || TaskManager.Instance.GetTaskAt(
                position,
                TaskType.Dismantle,
                layer) != null)
        {
            return false;
        }

        TaskManager.Instance.AddTask(
            position,
            TaskType.Dismantle,
            layer);
        return true;
    }

    public bool CanDismantleAt(Vector2Int position, GridLayer layer)
    {
        if (GridManager.Instance == null
            || !GridManager.Instance.IsInsideGrid(position.x, position.y))
        {
            return false;
        }

        if (layer == GridLayer.Structure)
        {
            UnityEngine.Object occupant =
                GridManager.Instance.GetOccupantAt(position, layer);
            if (!(occupant is IDismantlable)) return false;

            string id = GridManager.Instance.GetContentId(
                position.x,
                position.y,
                GridLayer.Structure);
            BuildDefinitionSO definition =
                BuildCatalogService.Instance?.GetById(id);
            return definition == null || definition.CanBeDismantled;
        }

        TileType tileType = GridManager.Instance.GetTileType(
            position.x,
            position.y,
            layer);

        if (layer == GridLayer.Terrain)
        {
            return tileType == TileType.Ladder
                || tileType == TileType.Chest
                || tileType == TileType.PrintingPod;
        }

        return tileType != TileType.Empty;
    }

    /// <summary>
    /// Executado pelo Colono via DismantleTaskHandler quando ele chega no local.
    /// </summary>
    public void ExecuteDismantleAt(int x, int y)
    {
        Vector2Int position = new Vector2Int(x, y);
        GridLayer layer = GridManager.Instance != null
            && GridManager.Instance.GetOccupantAt(
                position,
                GridLayer.Structure) != null
            ? GridLayer.Structure
            : GridLayer.Terrain;

        ExecuteDismantleAt(x, y, layer);
    }

    public void ExecuteDismantleAt(int x, int y, GridLayer layer)
    {
        if (GridManager.Instance == null) return;

        Vector2Int position = new Vector2Int(x, y);
        UnityEngine.Object occupant = GridManager.Instance.GetOccupantAt(
            position,
            layer);

        if (layer == GridLayer.Structure
            && occupant is StorageStructure storage)
        {
            StructureManager.Instance?.DismantleStructureAt(
                storage.GridPosition);
            return;
        }

        if (layer == GridLayer.Structure
            && occupant is PrintingPod printingPod)
        {
            StructureManager.Instance?.DismantleStructureAt(
                printingPod.GridPosition);
            return;
        }

        if (layer == GridLayer.Structure
            && occupant is ConfiguredStructure configured)
        {
            StructureManager.Instance?.DismantleStructureAt(
                configured.GridPosition);
            return;
        }

        TileType targetType = GridManager.Instance.GetTileType(x, y, layer);
        string contentId = GridManager.Instance.GetContentId(x, y, layer);
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(contentId);
        if (targetType == TileType.Empty) return;

        if (layer == GridLayer.Terrain && targetType == TileType.Chest)
        {
            StructureManager.Instance?.DismantleStructureAt(new Vector2Int(x, y));
        }
        else if (layer == GridLayer.Terrain
            && targetType == TileType.PrintingPod)
        {
            StructureManager.Instance?.DismantleStructureAt(
                new Vector2Int(x, y)
            );
        }
        else if (targetType == TileType.Ladder
            || layer == GridLayer.BackWall
            || layer == GridLayer.Decoration)
        {
            float cellSize = GridManager.Instance.cellSize;
            Vector3 spawnPos = new Vector3(x * cellSize + cellSize / 2f, y * cellSize + cellSize / 2f, 0);

            GridManager.Instance.SetTileType(
                x,
                y,
                TileType.Empty,
                layer);

            ResourceType reqResource = definition != null
                ? definition.RequiredResource
                : BuildingCosts.GetRequiredResource(targetType);
            int refundAmount = definition != null
                ? definition.RefundAmount
                : BuildingCosts.GetRefundAmount(targetType);

            if (refundAmount > 0)
            {
                ItemSpawner.Instance?.SpawnResource(
                    reqResource,
                    spawnPos,
                    refundAmount);
            }
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
