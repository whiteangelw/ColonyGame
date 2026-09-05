using UnityEngine;

[DisallowMultipleComponent]
public class HarvestableFoodSource : MonoBehaviour, IFoodSource
{
    [SerializeField] private FloraDefinitionSO definition;
    [SerializeField, Min(0)] private int availablePortions;

    private DuplicantController reservedBy;

    public string FloraId => definition != null ? definition.floraId : string.Empty;
    public int AvailablePortions => availablePortions;
    public Vector2Int GridPosition => GridManager.Instance != null
        ? GridManager.Instance.WorldToGridPosition(transform.position)
        : Vector2Int.zero;
    public bool IsEmergencyOnly => true;
    public bool HasFood => availablePortions > 0;

    private void OnEnable()
    {
        FoodSourceRegistry.Instance?.Register(this);
    }

    private void OnDisable()
    {
        reservedBy = null;
        FoodSourceRegistry.Instance?.Unregister(this);
    }

    public void Initialize(FloraDefinitionSO floraDefinition, int portions = -1)
    {
        definition = floraDefinition;
        availablePortions = portions >= 0
            ? portions
            : (definition != null ? definition.initialPortions : 1);
    }

    public bool TryReservePortion(DuplicantController duplicant)
    {
        if (duplicant == null || !HasFood
            || (reservedBy != null && reservedBy != duplicant))
        {
            return false;
        }

        reservedBy = duplicant;
        return true;
    }

    public bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood)
    {
        hungerRestored = 0f;
        isRawFood = definition == null || definition.isRawFood;

        if (duplicant == null || reservedBy != duplicant || !HasFood)
        {
            return false;
        }

        availablePortions--;
        hungerRestored = definition != null
            ? definition.hungerRestoredPerPortion
            : 25f;
        reservedBy = null;

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
        }
    }
}
