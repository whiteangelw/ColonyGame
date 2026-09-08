using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class HarvestableFoodSource : MonoBehaviour, IFoodSource
{
    [SerializeField] private FloraDefinitionSO definition;
    [SerializeField, Min(0)] private int availablePortions;

    private DuplicantController reservedBy;
    private int reservedPortions;

    public string FloraId => definition != null ? definition.floraId : string.Empty;
    public int AvailablePortions => availablePortions;
    public Vector2Int GridPosition => GridManager.Instance != null
        ? GridManager.Instance.WorldToGridPosition(transform.position)
        : Vector2Int.zero;
    public bool IsEmergencyOnly => true;
    public FoodSourceKind SourceKind => FoodSourceKind.Flora;
    public bool HasFood => availablePortions - reservedPortions > 0;

    private void OnEnable()
    {
        FoodSourceRegistry.Instance?.Register(this);
    }

    private void OnDisable()
    {
        reservedBy = null;
        reservedPortions = 0;
        FoodSourceRegistry.Instance?.Unregister(this);
    }

    public void Initialize(FloraDefinitionSO floraDefinition, int portions = -1)
    {
        definition = floraDefinition;
        availablePortions = portions >= 0
            ? portions
            : (definition != null ? definition.initialPortions : 1);
    }

    public void CollectFoodOptions(List<FoodOption> results)
    {
        if (results == null || !HasFood) return;

        results.Add(new FoodOption
        {
            resourceType = definition != null
                ? definition.foodResourceType
                : ResourceType.WildBerry,
            availablePortions = availablePortions - reservedPortions,
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

        reservedAmount = Mathf.Min(requestedPortions, availablePortions);
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
            || reservedPortions <= 0 || availablePortions <= 0)
        {
            return false;
        }

        availablePortions--;
        reservedPortions--;
        hungerRestored = definition != null
            ? definition.hungerRestoredPerPortion
            : 25f;
        if (reservedPortions <= 0) reservedBy = null;

        if (availablePortions <= 0)
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
