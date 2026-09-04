using UnityEngine;

[DisallowMultipleComponent]
public class DuplicantInventory : MonoBehaviour
{
    [Header("Configuração de Inventário")]
    public int maxCapacity = 10; // Capacidade máxima de carregamento

    public ResourceType? CarriedType { get; private set; }
    public int CarriedAmount { get; private set; } = 0;

    public bool HasItem => CarriedType.HasValue && CarriedAmount > 0;
    public bool IsFull => CarriedAmount >= maxCapacity;
    public int SpaceRemaining => Mathf.Max(0, maxCapacity - CarriedAmount);

    public int AddItem(ResourceType type, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        if (HasItem && CarriedType != type)
        {
            return 0;
        }

        if (!CarriedType.HasValue || CarriedAmount == 0)
        {
            CarriedType = type;
        }

        int acceptedAmount = Mathf.Min(amount, SpaceRemaining);
        CarriedAmount += acceptedAmount;

        StockpileManager.Instance?.RequestRefresh();

        return acceptedAmount;
    }

    public int RemoveItem(ResourceType type, int requestedAmount)
    {
        if (!HasItem
            || CarriedType != type
            || requestedAmount <= 0)
        {
            return 0;
        }

        int removedAmount = Mathf.Min(requestedAmount, CarriedAmount);
        CarriedAmount -= removedAmount;

        if (CarriedAmount <= 0)
        {
            CarriedType = null;
            CarriedAmount = 0;
        }

        StockpileManager.Instance?.RequestRefresh();
        return removedAmount;
    }

    public void Clear()
    {
        CarriedType = null;
        CarriedAmount = 0;
        StockpileManager.Instance?.RequestRefresh();
    }

    public bool DropCarriedItem(Vector3 dropPosition)
    {
        if (!HasItem || !CarriedType.HasValue) return true;

        ResourceType typeToDrop = CarriedType.Value;
        int amountToDrop = CarriedAmount;

        if (ItemSpawner.Instance == null
            || !ItemSpawner.Instance.TrySpawnResource(
                typeToDrop,
                dropPosition,
                amountToDrop))
        {
            Debug.LogError(
                $"[DuplicantInventory] Falha ao dropar {amountToDrop}x {typeToDrop}. O recurso permaneceu no inventário.",
                this
            );
            return false;
        }

        GameEvents.TriggerFloatingTextRequested(
            $"Dropou {amountToDrop}x {typeToDrop}",
            dropPosition,
            Color.orange
        );

        Clear();
        return true;
    }

    public void Restore(ResourceType? type, int amount)
    {
        CarriedType = amount > 0 ? type : null;
        CarriedAmount = CarriedType.HasValue
            ? Mathf.Max(0, amount)
            : 0;

        StockpileManager.Instance?.RequestRefresh();
    }
}
