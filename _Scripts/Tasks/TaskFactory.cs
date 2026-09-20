using UnityEngine;

public sealed class TaskFactory
{
    private TaskWorkSettingsSO workSettings;

    public void Configure(TaskWorkSettingsSO settings)
    {
        workSettings = settings;
    }

    public Task CreateTask(
        Vector2Int gridPosition,
        TaskType type,
        GridLayer targetLayer = GridLayer.Terrain)
    {
        Task task = new Task(
            gridPosition,
            type,
            TileType.Empty,
            null,
            GetPriority(type),
            targetLayer);
        ConfigureGenericWork(task);
        return task;
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

    public Task CreateHarvestTask(
        FloraEntity flora,
        int priorityOverride = -1)
    {
        if (flora == null || !flora.CanHarvest) return null;

        TaskType floraTaskType = flora.WorkTaskType;
        Task task = new Task(
            flora.GridPosition,
            floraTaskType,
            TileType.Empty,
            null,
            priorityOverride > 0
                ? priorityOverride
                : GetPriority(floraTaskType),
            GridLayer.Terrain,
            flora);

        task.ConfigureWork(
            flora.WorkRequired,
            flora.PreserveWorkOnInterruption);
        return task;
    }

    private static int GetPriority(TaskType taskType)
    {
        return PriorityManager.Instance != null
            ? PriorityManager.Instance.GetCategoryPriority(taskType)
            : 5;
    }

    private void ConfigureGenericWork(Task task)
    {
        if (task == null || workSettings == null) return;
        task.ConfigureWork(
            workSettings.GetWorkRequired(task.type),
            workSettings.PreservesProgress(task.type));
    }
}
