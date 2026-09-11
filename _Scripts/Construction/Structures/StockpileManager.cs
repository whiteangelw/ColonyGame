using System;
using System.Collections.Generic;
using UnityEngine;

public class StockpileManager : MonoBehaviour
{
    private static readonly ResourceType[] AllResourceTypes =
        (ResourceType[])Enum.GetValues(typeof(ResourceType));
    public static StockpileManager Instance { get; private set; }

    private readonly Dictionary<ResourceType, int> resources =
        new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> reservedResources =
        new Dictionary<ResourceType, int>();
    private readonly HashSet<ResourceType> unlockedResources =
        new HashSet<ResourceType>();
    private readonly Dictionary<ResourceType, int> refreshTotals =
        new Dictionary<ResourceType, int>();
    private readonly List<ResourceItem> groundItemBuffer =
        new List<ResourceItem>();

    [Header("Performance")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    private bool refreshPending;
    private float nextAllowedRefreshTime;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        foreach (ResourceType type in AllResourceTypes)
        {
            resources[type] = 0;
            reservedResources[type] = 0;
            refreshTotals[type] = 0;
        }
    }

    private void Start()
    {
        RefreshReachableResources();
    }

    private void LateUpdate()
    {
        if (!refreshPending
            || Time.unscaledTime < nextAllowedRefreshTime)
        {
            return;
        }

        refreshPending = false;
        RefreshReachableResources();
    }

    public void RequestRefresh()
    {
        refreshPending = true;
    }

    public void ResetRuntimeReservationsForLoad()
    {
        foreach (ResourceType type in AllResourceTypes)
        {
            reservedResources[type] = 0;
        }

        refreshPending = false;
    }

    /// <summary>
    /// Recalcula o total usando somente recursos físicos alcançáveis.
    /// Mover um item entre chão, inventário e baú não muda o total.
    /// </summary>
    public void RefreshReachableResources()
    {
        refreshPending = false;
        nextAllowedRefreshTime = Time.unscaledTime + refreshInterval;

        ResetRefreshTotals();

        CountReachableGroundItems(refreshTotals);
        CountReachableStorages(refreshTotals);
        CountDuplicantInventories(refreshTotals);

        foreach (ResourceType type in AllResourceTypes)
        {
            int previousAmount = resources[type];
            int newAmount = refreshTotals[type];

            resources[type] = newAmount;
            reservedResources[type] = Mathf.Min(
                reservedResources[type],
                newAmount
            );

            if (newAmount > 0 && unlockedResources.Add(type))
            {
                GameEvents.TriggerResourceUnlocked(type);
            }

            if (previousAmount != newAmount)
            {
                GameEvents.TriggerResourceAmountChanged(type, newAmount);
            }
        }
    }

    private void ResetRefreshTotals()
    {
        foreach (ResourceType type in AllResourceTypes)
        {
            refreshTotals[type] = 0;
        }
    }

    private void CountReachableGroundItems(
        Dictionary<ResourceType, int> totals)
    {
        ResourceItem.CopyActiveItemsTo(groundItemBuffer);

        foreach (ResourceItem item in groundItemBuffer)
        {
            if (item == null || item.amount <= 0)
            {
                continue;
            }

            Vector2Int position = GridManager.Instance != null
                ? GridManager.Instance.WorldToGridPosition(
                    item.transform.position
                )
                : Vector2Int.zero;

            if (IsReachable(position))
            {
                totals[item.type] += item.amount;
            }
        }
    }

    private void CountReachableStorages(
        Dictionary<ResourceType, int> totals)
    {
        if (StructureManager.Instance == null)
        {
            return;
        }

        foreach (StorageStructure storage
                 in StructureManager.Instance.GetRegisteredStorages())
        {
            if (storage == null
                || !IsReachable(storage.GridPosition))
            {
                continue;
            }

            foreach (var entry in storage.GetAllStoredItems())
            {
                totals[entry.Key] += entry.Value;
            }
        }
    }

    private void CountDuplicantInventories(
        Dictionary<ResourceType, int> totals)
    {
        PerformanceMetricsService.RecordGlobalObjectSearch();
        DuplicantInventory[] inventories =
            FindObjectsByType<DuplicantInventory>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (DuplicantInventory inventory in inventories)
        {
            if (inventory != null
                && inventory.HasItem
                && inventory.CarriedType.HasValue)
            {
                totals[inventory.CarriedType.Value] +=
                    inventory.CarriedAmount;
            }
        }
    }

    private bool IsReachable(Vector2Int position)
    {
        return ReachabilityManager.Instance == null
            || !ReachabilityManager.Instance.IsReady
            || ReachabilityManager.Instance.CanAnyDuplicantReach(position);
    }

    public bool HasResource(ResourceType type, int amount)
    {
        return amount > 0 && GetAvailableAmount(type) >= amount;
    }

    public int GetAvailableAmount(ResourceType type)
    {
        int total = GetAmount(type);
        int reserved = reservedResources.TryGetValue(
            type,
            out int reservedAmount
        ) ? reservedAmount : 0;

        return Mathf.Max(0, total - reserved);
    }

    public bool ReserveResource(ResourceType type, int amount)
    {
        if (!HasResource(type, amount))
        {
            return false;
        }

        reservedResources[type] += amount;
        return true;
    }

    public void UnreserveResource(ResourceType type, int amount)
    {
        if (amount <= 0 || !reservedResources.ContainsKey(type))
        {
            return;
        }

        reservedResources[type] = Mathf.Max(
            0,
            reservedResources[type] - amount
        );
    }

    public bool CommitReservedResource(ResourceType type, int amount)
    {
        if (amount <= 0
            || !reservedResources.TryGetValue(type, out int reserved)
            || reserved < amount)
        {
            return false;
        }

        reservedResources[type] -= amount;
        return true;
    }

    public int GetAmount(ResourceType type)
    {
        return resources.TryGetValue(type, out int amount) ? amount : 0;
    }

    public bool HasUnlockedResource(ResourceType type)
    {
        return unlockedResources.Contains(type);
    }
}
