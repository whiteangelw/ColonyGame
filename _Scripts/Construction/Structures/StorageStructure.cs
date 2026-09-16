using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StorageStructure : MonoBehaviour, IStorage, IFoodSource, IDismantlable, IInteractable, IConfiguredStructureBehaviour
{
    public bool UsesSpecializedSaveData => true;
    [Header("Posição no Grid")]
    [SerializeField] private Vector2Int gridPosition;
    public Vector2Int GridPosition => gridPosition;

    [Header("Configuração de Capacidade")]
    public int maxCapacityPerResource = 50;

    public bool IsBeingDismantled { get; private set; } = false;

    private readonly Dictionary<ResourceType, int> localInventory = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedSpace = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedFood = new Dictionary<ResourceType, int>();
    private sealed class FoodReservation
    {
        public ResourceType type;
        public int remainingPortions;
    }

    private readonly Dictionary<DuplicantController, FoodReservation> foodReservations =
        new Dictionary<DuplicantController, FoodReservation>();

    public bool IsEmergencyOnly => false;
    public FoodSourceKind SourceKind => FoodSourceKind.Storage;
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

    public void InitializeStructureBehaviour(ConfiguredStructure structure)
    {
        if (structure == null) return;
        SetGridPosition(structure.GridPosition);
        StructureManager.Instance?.RegisterStorage(structure.GridPosition, this);
    }

    public void ShutdownStructureBehaviour()
    {
        StructureManager.Instance?.UnregisterStorage(gridPosition);
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

            // O item ainda será transferido para o inventário do duplicant.
            // Adie o recálculo para evitar uma janela em que o recurso não
            // aparece nem no baú nem no inventário.
            StockpileManager.Instance?.RequestRefresh();
            return true;
        }
        return false;
    }

    public void CollectFoodOptions(List<FoodOption> results)
    {
        if (results == null || IsBeingDismantled) return;

        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            int available = GetLocalAmount(type) - GetReservedFood(type);
            ItemDataSO data = ItemSpawner.Instance?.GetItemData(type);
            if (available <= 0 || data == null || !data.isFood) continue;

            results.Add(new FoodOption
            {
                resourceType = type,
                availablePortions = available,
                hungerRestoredPerPortion = data.hungerRestored,
                isRawFood = data.isRawFood,
                quality = data.foodQuality
            });
        }
    }

    public bool TryReserveMeal(
        DuplicantController duplicant,
        ResourceType foodType,
        int requestedPortions,
        out int reservedAmount)
    {
        reservedAmount = 0;
        if (duplicant == null || IsBeingDismantled || requestedPortions <= 0)
        {
            return false;
        }

        if (foodReservations.TryGetValue(duplicant, out FoodReservation existing))
        {
            reservedAmount = existing.remainingPortions;
            return existing.type == foodType && reservedAmount > 0;
        }

        int available = GetLocalAmount(foodType) - GetReservedFood(foodType);
        reservedAmount = Mathf.Min(requestedPortions, available);
        if (reservedAmount <= 0) return false;

        foodReservations[duplicant] = new FoodReservation
        {
            type = foodType,
            remainingPortions = reservedAmount
        };
        reservedFood[foodType] = GetReservedFood(foodType) + reservedAmount;
        return true;
    }

    public int GetReservedPortionCount(DuplicantController duplicant)
    {
        return duplicant != null
            && foodReservations.TryGetValue(duplicant, out FoodReservation reservation)
                ? reservation.remainingPortions
                : 0;
    }

    public bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood)
    {
        hungerRestored = 0f;
        isRawFood = false;

        if (duplicant == null
            || !foodReservations.TryGetValue(duplicant, out FoodReservation reservation)
            || reservation.remainingPortions <= 0)
        {
            return false;
        }

        ResourceType type = reservation.type;
        ItemDataSO data = ItemSpawner.Instance?.GetItemData(type);
        bool canConsume = !IsBeingDismantled
            && data != null
            && data.isFood
            && GetLocalAmount(type) > 0;

        if (!canConsume)
        {
            ReleaseReservation(duplicant);
            return false;
        }

        localInventory[type]--;
        reservation.remainingPortions--;
        reservedFood[type] = Mathf.Max(0, GetReservedFood(type) - 1);
        if (reservation.remainingPortions <= 0)
        {
            foodReservations.Remove(duplicant);
        }
        hungerRestored = Mathf.Max(0f, data.hungerRestored);
        isRawFood = data.isRawFood;
        GameEvents.TriggerChestUpdated(gridPosition);
        StockpileManager.Instance?.RequestRefresh();
        return true;
    }

    public void ReleaseReservation(DuplicantController duplicant)
    {
        if (duplicant == null
            || !foodReservations.TryGetValue(duplicant, out FoodReservation reservation))
        {
            return;
        }

        foodReservations.Remove(duplicant);
        reservedFood[reservation.type] = Mathf.Max(
            0,
            GetReservedFood(reservation.type) - reservation.remainingPortions);
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
