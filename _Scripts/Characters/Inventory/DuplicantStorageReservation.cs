using UnityEngine;

/// <summary>
/// Controla uma única reserva de espaço em armazenamento.
/// Não procura baús e não decide quais itens transportar.
/// </summary>
public sealed class DuplicantStorageReservation
{
    private StorageStructure storage;
    private ResourceType resourceType;
    private int reservedAmount;
    private Vector2Int storagePosition;

    public bool IsActive =>
        storage != null && reservedAmount > 0;

    public StorageStructure Storage => storage;
    public ResourceType ResourceType => resourceType;
    public int ReservedAmount => reservedAmount;
    public Vector2Int StoragePosition => storagePosition;

    /// <summary>
    /// Registra uma reserva que já foi criada pelo StructureManager.
    /// </summary>
    public bool Track(
        IStorage targetStorage,
        ResourceType type,
        int amount)
    {
        StorageStructure target =
            targetStorage as StorageStructure;

        if (target == null || amount <= 0)
        {
            return false;
        }

        storage = target;
        resourceType = type;
        reservedAmount = amount;
        storagePosition = target.GridPosition;

        return true;
    }

    public bool IsValidFor(DuplicantInventory inventory)
    {
        if (!IsActive
            || inventory == null
            || !inventory.HasItem
            || !inventory.CarriedType.HasValue
            || inventory.CarriedType.Value != resourceType)
        {
            return false;
        }

        return !storage.IsBeingDismantled
            && storage.GetReservedSpace(resourceType)
                >= reservedAmount;
    }

    /// <summary>
    /// Aumenta a reserva no mesmo baú.
    /// </summary>
    public bool TryExpand(int amount)
    {
        if (storage == null
            || amount <= 0
            || !storage.TryReserveSpace(resourceType, amount))
        {
            return false;
        }

        reservedAmount += amount;
        return true;
    }

    /// <summary>
    /// Libera apenas parte do espaço reservado.
    /// </summary>
    public void ReleaseAmount(int amount)
    {
        if (storage == null
            || amount <= 0
            || reservedAmount <= 0)
        {
            return;
        }

        int amountToRelease = Mathf.Min(
            amount,
            reservedAmount);

        storage.ReleaseReservedSpace(
            resourceType,
            amountToRelease);

        reservedAmount -= amountToRelease;

        if (reservedAmount <= 0)
        {
            ClearTracking();
        }
    }

    /// <summary>
    /// Move os itens do inventário para o espaço reservado.
    /// </summary>
    public bool TryStore(DuplicantInventory inventory)
    {
        if (!IsActive
            || inventory == null
            || !inventory.HasItem
            || inventory.CarriedType != resourceType)
        {
            return false;
        }

        int amountToStore = inventory.CarriedAmount;

        int removedAmount = inventory.RemoveItem(
            resourceType,
            amountToStore);

        if (removedAmount != amountToStore)
        {
            if (removedAmount > 0)
            {
                inventory.AddItem(
                    resourceType,
                    removedAmount);
            }

            return false;
        }

        if (storage.StoreReservedItem(
                resourceType,
                amountToStore))
        {
            return true;
        }

        // A entrega falhou: devolve os itens ao inventário.
        inventory.AddItem(
            resourceType,
            amountToStore);

        return false;
    }

    /// <summary>
    /// Libera todo o espaço que ainda está reservado.
    /// </summary>
    public void Release()
    {
        if (storage != null && reservedAmount > 0)
        {
            storage.ReleaseReservedSpace(
                resourceType,
                reservedAmount);
        }

        ClearTracking();
    }

    /// <summary>
    /// Limpa o controle local após uma entrega confirmada.
    /// Não libera espaço porque StoreReservedItem já o consumiu.
    /// </summary>
    public void Complete()
    {
        ClearTracking();
    }

    private void ClearTracking()
    {
        storage = null;
        reservedAmount = 0;
        storagePosition = Vector2Int.zero;
    }
}