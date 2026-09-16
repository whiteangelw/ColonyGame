using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class HarvestableFoodSource : FloraEntity, IFoodSource
{
    private DuplicantController reservedBy;
    private int reservedPortions;

    public int AvailablePortions => availableUnits;
    public bool IsEmergencyOnly => true;
    public FoodSourceKind SourceKind => FoodSourceKind.Flora;
    public bool HasFood => availableUnits - reservedPortions > 0;

    private void OnEnable()
    {
        FoodSourceRegistry.Instance?.Register(this);
    }

    protected override void OnDisable()
    {
        reservedBy = null;
        reservedPortions = 0;
        FoodSourceRegistry.Instance?.Unregister(this);
        base.OnDisable();
    }

    public override void Initialize(FloraDefinitionSO floraDefinition, int portions = -1)
    {
        base.Initialize(floraDefinition, portions);
    }

    public void CollectFoodOptions(List<FoodOption> results)
    {
        if (results == null || !HasFood) return;

        results.Add(new FoodOption
        {
            resourceType = definition != null
                ? definition.foodResourceType
                : ResourceType.WildBerry,
            availablePortions = availableUnits - reservedPortions,
            hungerRestoredPerPortion = definition != null
                ? definition.hungerRestoredPerPortion
                : 25f,
            isRawFood = definition == null || definition.isRawFood,
            quality = definition != null ? definition.foodQuality : 0
        });
    }

    public bool TryReserveMeal(
        DuplicantController duplicant,
        ResourceType foodType,
        int requestedPortions,
        out int reservedAmount)
    {
        reservedAmount = 0;
        if (duplicant == null || requestedPortions <= 0
            || (reservedBy != null && reservedBy != duplicant))
        {
            return false;
        }

        ResourceType availableType = definition != null
            ? definition.foodResourceType
            : ResourceType.WildBerry;
        if (availableType != foodType) return false;

        if (reservedBy == duplicant)
        {
            reservedAmount = reservedPortions;
            return reservedAmount > 0;
        }

        reservedAmount = Mathf.Min(requestedPortions, availableUnits);
        if (reservedAmount <= 0) return false;

        reservedBy = duplicant;
        reservedPortions = reservedAmount;
        return true;
    }

    public int GetReservedPortionCount(DuplicantController duplicant)
    {
        return reservedBy == duplicant ? reservedPortions : 0;
    }

    public bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood)
    {
        hungerRestored = 0f;
        isRawFood = definition == null || definition.isRawFood;

        if (duplicant == null || reservedBy != duplicant
            || reservedPortions <= 0 || availableUnits <= 0)
        {
            return false;
        }

        availableUnits--;
        reservedPortions--;
        hungerRestored = definition != null
            ? definition.hungerRestoredPerPortion
            : 25f;
        if (reservedPortions <= 0) reservedBy = null;

        if (availableUnits <= 0)
        {
            FoodSourceRegistry.Instance?.Unregister(this);
            Destroy(gameObject);
        }

        return true;
    }

    public void ReleaseReservation(DuplicantController duplicant)
    {
        if (reservedBy == duplicant)
        {
            reservedBy = null;
            reservedPortions = 0;
        }
    }
}
