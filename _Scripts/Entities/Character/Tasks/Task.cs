using UnityEngine;

public enum TaskType
{
    Dig,
    BuildTile,
    HaulResource,
    Dismantle
}

public class Task
{
    public Vector2Int gridPosition;
    public TaskType type;
    public TileType buildTileType;
    public bool isAssigned;
    public ResourceItem targetItem;
    public ConstructionBlueprint targetBlueprint;
    public int priority;

    public Task(Vector2Int gridPosition, TaskType type, TileType buildTileType = TileType.Ladder, ResourceItem item = null, int priority = 5)
    {
        this.gridPosition = gridPosition;
        this.type = type;
        this.buildTileType = buildTileType;
        this.isAssigned = false;
        this.targetItem = item;
        this.priority = Mathf.Clamp(priority, 1, 9);
    }
}