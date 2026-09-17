using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanterCrop : FloraEntity
{
    private PlanterStructureBehaviour owner;

    public override bool CanHarvest => owner != null && owner.IsCropReady;

    public void Bind(
        PlanterStructureBehaviour planter,
        FloraDefinitionSO cropDefinition)
    {
        owner = planter;
        Initialize(cropDefinition, 0);
        FloraManager.Instance?.RegisterStructureCrop(this);
    }

    private void OnEnable()
    {
        if (owner != null)
        {
            FloraManager.Instance?.RegisterStructureCrop(this);
        }
    }

    protected override void OnDisable()
    {
        FloraManager.Instance?.UnregisterStructureCrop(this);
        base.OnDisable();
    }

    public void SetReady(bool ready)
    {
        availableUnits = ready && definition != null
            ? Mathf.Max(1, definition.initialPortions)
            : 0;
    }

    public override bool TryHarvest(
        out ResourceType resourceType,
        out int harvestedAmount)
    {
        resourceType = definition != null
            ? definition.foodResourceType
            : ResourceType.WildBerry;
        harvestedAmount = 0;

        if (!CanHarvest || definition == null)
        {
            return false;
        }

        harvestedAmount = Mathf.Min(
            Mathf.Max(1, definition.harvestUnitsPerAction),
            availableUnits);
        availableUnits -= harvestedAmount;

        if (availableUnits <= 0)
        {
            owner.HandleCropHarvested();
        }

        return harvestedAmount > 0;
    }
}
