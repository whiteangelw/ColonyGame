using UnityEngine;

public sealed class TaskFactory
{
    public Task CreateTask(
        Vector2Int gridPosition,
        TaskType type,
        GridLayer targetLayer = GridLayer.Terrain)
    {
        return new Task(
            gridPosition,
            type,
            TileType.Empty,
            null,
            GetPriority(type),
            targetLayer);
    }

    public Task CreateBuildTask(
        Vector2Int gridPosition,
        TileType tileType)
    {
        return new Task(
            gridPosition,
            TaskType.BuildTile,
            tileType,
            null,
            GetPriority(TaskType.BuildTile));
    }

    public Task CreateGroundHaulTask(ResourceItem item)
    {
        if (item == null
            || !item.IsReadyForHaul
            || GridManager.Instance == null)
        {
            return null;
        }

        Vector2Int gridPosition =
            GridManager.Instance.WorldToGridPosition(
                item.transform.position);

        return new Task(
            gridPosition,
            TaskType.HaulResource,
            TileType.Empty,
            item,
            GetPriority(TaskType.HaulResource));
    }

    private static int GetPriority(TaskType taskType)
    {
        return PriorityManager.Instance != null
            ? PriorityManager.Instance.GetCategoryPriority(taskType)
            : 5;
    }
}