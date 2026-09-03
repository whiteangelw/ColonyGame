using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StorageStructure : MonoBehaviour, IStorage, IDismantlable, IInteractable
{
    [Header("Posição no Grid")]
    [SerializeField] private Vector2Int gridPosition;
    public Vector2Int GridPosition => gridPosition;

    [Header("Configuração de Capacidade")]
    public int maxCapacityPerResource = 50;

    public bool IsBeingDismantled { get; private set; } = false;

    private readonly Dictionary<ResourceType, int> localInventory = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedSpace = new Dictionary<ResourceType, int>();

    private void Awake()
    {
        InitializeInventory();
    }

    private void InitializeInventory()
    {
        localInventory.Clear();
        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            localInventory[type] = 0;
            reservedSpace[type] = 0;
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
        if (current >= amount)
        {
            localInventory[type] -= amount;
            GameEvents.TriggerChestUpdated(gridPosition);
            StockpileManager.Instance?.RefreshReachableResources();
            return true;
        }
        return false;
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
        }

        return drainedItems;
    }

    public Dictionary<ResourceType, int> GetAllStoredItems()
    {
        return new Dictionary<ResourceType, int>(localInventory);
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
