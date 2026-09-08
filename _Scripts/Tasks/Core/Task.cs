using UnityEngine;

public enum TaskType
{
    Dig,
    BuildTile,
    HaulResource,
    Dismantle
}

public enum GroundHaulPhase
{
    AwaitingPickup,
    CarryingToStorage
}

public class Task
{
    public Vector2Int gridPosition;
    public readonly Vector2Int visualGridPosition;
    public TaskType type;
    public TileType buildTileType;
    public bool isAssigned;
    public ResourceItem targetItem;
    public ConstructionBlueprint targetBlueprint;
    public GridLayer targetLayer;
    public int priority;
    public GroundHaulPhase groundHaulPhase { get; private set; }
    public DuplicantTaskRunner groundHaulRunner { get; private set; }

    public Task(
        Vector2Int gridPosition,
        TaskType type,
        TileType buildTileType = TileType.Ladder,
        ResourceItem item = null,
        int priority = 5,
        GridLayer targetLayer = GridLayer.Terrain)
    {
        this.gridPosition = gridPosition;
        this.visualGridPosition = gridPosition;
        this.type = type;
        this.buildTileType = buildTileType;
        this.isAssigned = false;
        this.targetItem = item;
        this.targetLayer = targetLayer;
        this.priority = Mathf.Clamp(priority, 1, 9);
        this.groundHaulPhase = GroundHaulPhase.AwaitingPickup;
    }

    public void MarkGroundHaulCarrying(DuplicantTaskRunner runner)
    {
        if (type != TaskType.HaulResource || targetBlueprint != null)
        {
            return;
        }

        groundHaulRunner = runner;
        groundHaulPhase = GroundHaulPhase.CarryingToStorage;
    }

    public void ResetGroundHaulProgress()
    {
        groundHaulRunner = null;
        groundHaulPhase = GroundHaulPhase.AwaitingPickup;
    }
}
