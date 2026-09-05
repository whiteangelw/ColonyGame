using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StorageStructure : MonoBehaviour, IStorage, IFoodSource, IDismantlable, IInteractable
{
    [Header("Posição no Grid")]
    [SerializeField] private Vector2Int gridPosition;
    public Vector2Int GridPosition => gridPosition;

    [Header("Configuração de Capacidade")]
    public int maxCapacityPerResource = 50;

    public bool IsBeingDismantled { get; private set; } = false;

    private readonly Dictionary<ResourceType, int> localInventory = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedSpace = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedFood = new Dictionary<ResourceType, int>();
    private readonly Dictionary<DuplicantController, ResourceType> foodReservations =
        new Dictionary<DuplicantController, ResourceType>();

    public bool IsEmergencyOnly => false;
    public bool HasFood => FindBestAvailableFood(out _, out _);

    private void OnEnable()
    {
        FoodSourceRegistry.Instance?.Register(this);
    }

    private void OnDisable()
    {
        FoodSourceRegistry.Instance?.Unregister(this);
        foodReservations.Clear();

        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            reservedFood[type] = 0;
        }
    }

    private void Awake()
    {
        InitializeInventory();
    }

    private void InitializeInventory()
    {
        localInventory.Clear();
        reservedSpace.Clear();
        reservedFood.Clear();
        foodReservations.Clear();
        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            localInventory[type] = 0;
            reservedSpace[type] = 0;
            reservedFood[type] = 0;
        }
    }

    public void SetGridPosition(Vector2Int pos)
    {
        gridPosition = pos;
    }

    public bool CanStoreItem(ResourceType type, int amount)
    {
        if (IsBeingDismantled || amount <= 0) return false;
        int current = GetLocalAmount(type);
        int reserved = GetReservedSpace(type);
        return current + reserved + amount <= maxCapacityPerResource;
    }

    public bool TryReserveSpace(ResourceType type, int amount)
    {
        if (!CanStoreItem(type, amount)) return false;

        reservedSpace[type] += amount;
        return true;
    }

    public void ReleaseReservedSpace(ResourceType type, int amount)
    {
        if (amount <= 0 || !reservedSpace.ContainsKey(type)) return;

        reservedSpace[type] = Mathf.Max(0, reservedSpace[type] - amount);
        StructureManager.Instance?.QueueExistingGroundItems();
    }

    public bool StoreItem(ResourceType type, int amount)
    {
        if (IsBeingDismantled || !CanStoreItem(type, amount)) return false;

        localInventory[type] += amount;
        GameEvents.TriggerChestUpdated(gridPosition);
        StockpileManager.Instance?.RefreshReachableResources();
        return true;
    }

    public bool StoreReservedItem(ResourceType type, int amount)
    {
        if (IsBeingDismantled || amount <= 0) return false;

        int reserved = GetReservedSpace(type);
        int current = GetLocalAmount(type);

        if (reserved < amount
            || current + amount > maxCapacityPerResource)
        {
            return false;
        }

        reservedSpace[type] -= amount;
        localInventory[type] += amount;

        GameEvents.TriggerChestUpdated(gridPosition);
        StockpileManager.Instance?.RefreshReachableResources();
        return true;
    }

    public bool WithdrawItem(ResourceType type, int amount)
    {
        if (IsBeingDismantled || amount <= 0) return false;
        int current = GetLocalAmount(type);
        int available = current - GetReservedFood(type);
        if (available >= amount)
        {
            localInventory[type] -= amount;
            GameEvents.TriggerChestUpdated(gridPosition);
            StockpileManager.Instance?.RefreshReachableResources();
            return true;
        }
        return false;
    }

    public bool TryReservePortion(DuplicantController duplicant)
    {
        if (duplicant == null || IsBeingDismantled) return false;
        if (foodReservations.ContainsKey(duplicant)) return true;
        if (!FindBestAvailableFood(out ResourceType type, out _)) return false;

        foodReservations[duplicant] = type;
        reservedFood[type] = GetReservedFood(type) + 1;
        return true;
    }

    public bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood)
    {
        hungerRestored = 0f;
        isRawFood = false;

        if (duplicant == null
            || !foodReservations.TryGetValue(duplicant, out ResourceType type))
        {
            return false;
        }

        ItemDataSO data = ItemSpawner.Instance?.GetItemData(type);
        bool canConsume = !IsBeingDismantled
            && data != null
            && data.isFood
            && GetLocalAmount(type) > 0;

        ReleaseReservation(duplicant);
        if (!canConsume) return false;

        localInventory[type]--;
        hungerRestored = Mathf.Max(0f, data.hungerRestored);
        isRawFood = data.isRawFood;
        GameEvents.TriggerChestUpdated(gridPosition);
        StockpileManager.Instance?.RequestRefresh();
        return true;
    }

    public void ReleaseReservation(DuplicantController duplicant)
    {
        if (duplicant == null
            || !foodReservations.TryGetValue(duplicant, out ResourceType type))
        {
            return;
        }

        foodReservations.Remove(duplicant);
        reservedFood[type] = Mathf.Max(0, GetReservedFood(type) - 1);
    }

    private bool FindBestAvailableFood(
        out ResourceType selectedType,
        out ItemDataSO selectedData)
    {
        selectedType = default;
        selectedData = null;
        float bestScore = -1f;

        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            int available = GetLocalAmount(type) - GetReservedFood(type);
            ItemDataSO data = ItemSpawner.Instance?.GetItemData(type);
            if (available <= 0 || data == null || !data.isFood) continue;

            float score = data.hungerRestored
                + (data.isRawFood ? 0f : 10000f);
            if (score <= bestScore) continue;

            selectedType = type;
            selectedData = data;
            bestScore = score;
        }

        return selectedData != null;
    }

    private int GetReservedFood(ResourceType type)
    {
        return reservedFood.TryGetValue(type, out int amount) ? amount : 0;
    }

    public int GetLocalAmount(ResourceType type)
    {
        return localInventory.TryGetValue(type, out int amount) ? amount : 0;
    }

    public int GetReservedSpace(ResourceType type)
    {
        return reservedSpace.TryGetValue(type, out int amount) ? amount : 0;
    }

    public Dictionary<ResourceType, int> DrainForDismantle()
    {
        Dictionary<ResourceType, int> drainedItems = GetAllStoredItems();

        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            localInventory[type] = 0;
            reservedSpace[type] = 0;
            reservedFood[type] = 0;
        }

        foodReservations.Clear();

        return drainedItems;
    }

    public Dictionary<ResourceType, int> GetAllStoredItems()
    {
        return new Dictionary<ResourceType, int>(localInventory);
    }

    public void RestoreStoredItems(
        IReadOnlyDictionary<ResourceType, int> restoredItems)
    {
        InitializeInventory();

        if (restoredItems != null)
        {
            foreach (var entry in restoredItems)
            {
                localInventory[entry.Key] = Mathf.Clamp(
                    entry.Value,
                    0,
                    maxCapacityPerResource
                );
            }
        }

        IsBeingDismantled = false;
        GameEvents.TriggerChestUpdated(gridPosition);
    }

    public void RestoreDismantleState(bool isBeingDismantled)
    {
        IsBeingDismantled = isBeingDismantled;
    }

    public void OnInteract()
    {
        if (IsBeingDismantled) return;

        if (ChestInspectUI.Instance != null)
        {
            ChestInspectUI.Instance.OpenPanel(gridPosition);
        }
    }

    public bool MarkAsDismantled()
    {
        if (IsBeingDismantled) return false;
        IsBeingDismantled = true;
        return true;
    }

    public void Dismantle()
    {
        WorldInteractionService.Instance?.DismantleTile(
            gridPosition.x,
            gridPosition.y
        );
    }
}
