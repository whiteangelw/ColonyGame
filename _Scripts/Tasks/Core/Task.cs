using UnityEngine;

public enum TaskType
{
    Dig,
    BuildTile,
    HaulResource,
    Dismantle,
    Harvest,
    Chop,
    // Sempre acrescente novos tipos no final: saves persistem o valor numérico.
    OperateMachine
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
    public FloraEntity targetFlora;
    public ProductionMachineBehaviour targetProductionMachine;
    public IResourceDeliveryTarget targetResourceDelivery;
    public ResourceType? requestedResourceType;
    public GridLayer targetLayer;
    public int priority;
    public float workRequired { get; private set; }
    public float workCompleted { get; private set; }
    public bool preserveWorkOnInterruption { get; private set; }
    public GroundHaulPhase groundHaulPhase { get; private set; }
    public DuplicantTaskRunner groundHaulRunner { get; private set; }

    public Task(
        Vector2Int gridPosition,
        TaskType type,
        TileType buildTileType = TileType.Ladder,
        ResourceItem item = null,
        int priority = 5,
        GridLayer targetLayer = GridLayer.Terrain,
        FloraEntity flora = null)
    {
        this.gridPosition = gridPosition;
        this.visualGridPosition = gridPosition;
        this.type = type;
        this.buildTileType = buildTileType;
        this.isAssigned = false;
        this.targetItem = item;
        this.targetFlora = flora;
        this.targetLayer = targetLayer;
        this.priority = Mathf.Clamp(priority, 1, 9);
        this.groundHaulPhase = GroundHaulPhase.AwaitingPickup;
        ConfigureWork(1f, false);
    }

    public void ConfigureWork(float required, bool preserveOnInterruption)
    {
        workRequired = Mathf.Max(0.1f, required);
        preserveWorkOnInterruption = preserveOnInterruption;
        workCompleted = Mathf.Clamp(workCompleted, 0f, workRequired);
    }

    public bool ApplyWork(float amount)
    {
        if (amount <= 0f) return workCompleted >= workRequired;
        workCompleted = Mathf.Min(workRequired, workCompleted + amount);
        return workCompleted >= workRequired;
    }

    public void HandleWorkInterrupted()
    {
        if (!preserveWorkOnInterruption) workCompleted = 0f;
    }

    public void RestoreWork(float required, float completed, bool preserve)
    {
        ConfigureWork(required, preserve);
        workCompleted = Mathf.Clamp(completed, 0f, workRequired);
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
