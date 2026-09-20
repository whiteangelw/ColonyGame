using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FloraEntity : MonoBehaviour
{
    [SerializeField] protected FloraDefinitionSO definition;
    [SerializeField, Min(0)] protected int availableUnits;

    public string FloraId => definition != null ? definition.floraId : string.Empty;
    public int AvailableUnits => availableUnits;
    public Vector2Int GridPosition => GridManager.Instance != null
        ? GridManager.Instance.WorldToGridPosition(transform.position)
        : Vector2Int.zero;
    public FloraDefinitionSO Definition => definition;
    public TaskType WorkTaskType => definition != null
        && definition.actionType == FloraActionType.Chop
            ? TaskType.Chop
            : TaskType.Harvest;
    public virtual bool CanHarvest => availableUnits > 0 && definition != null;
    public virtual float WorkRequired => definition != null
        ? definition.HarvestWorkRequired
        : 1f;
    public virtual bool PreserveWorkOnInterruption => definition != null
        && definition.preserveWorkOnInterruption;

    protected virtual void OnDisable()
    {
        FloraManager.Instance?.Unregister(this);
    }

    public virtual void Initialize(FloraDefinitionSO floraDefinition, int units = -1)
    {
        definition = floraDefinition;
        availableUnits = units >= 0
            ? units
            : (definition != null ? definition.initialPortions : 1);
    }

    public virtual bool TryHarvest(
        out ResourceType resourceType,
        out int harvestedAmount)
    {
        resourceType = definition != null
            ? definition.foodResourceType
            : default;
        harvestedAmount = 0;
        if (!CanHarvest) return false;

        harvestedAmount = Mathf.Min(
            definition != null
                ? Mathf.Max(1, definition.harvestUnitsPerAction)
                : 1,
            availableUnits);
        availableUnits -= harvestedAmount;
        if (availableUnits <= 0) Destroy(gameObject);
        return harvestedAmount > 0;
    }

    public virtual bool TryHarvest(out List<ResourceAmount> results)
    {
        results = new List<ResourceAmount>();
        if (!CanHarvest) return false;

        int harvestedUnits = Mathf.Min(
            definition != null
                ? Mathf.Max(1, definition.harvestUnitsPerAction)
                : 1,
            availableUnits);
        availableUnits -= harvestedUnits;

        if (definition != null && definition.HasConfiguredYields)
        {
            foreach (FloraYieldEntry entry in definition.yields)
            {
                if (entry == null) continue;
                int amount = entry.Roll();
                if (amount > 0)
                {
                    results.Add(new ResourceAmount(entry.resourceType, amount));
                }
            }
        }
        else if (harvestedUnits > 0)
        {
            results.Add(new ResourceAmount(
                definition != null ? definition.foodResourceType : default,
                harvestedUnits));
        }

        if (availableUnits <= 0) Destroy(gameObject);
        return results.Count > 0;
    }
}
