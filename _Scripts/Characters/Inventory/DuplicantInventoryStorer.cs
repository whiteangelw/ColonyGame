using System.Collections;
using UnityEngine;

public enum InventoryStoreResult
{
    NothingToStore,
    Stored,
    StorageUnavailable,
    ReservationTrackingFailed,
    PathUnavailable,
    StorageRejected
}

/// <summary>
/// Guarda a carga atual do duplicant em um armazenamento alcançável.
/// </summary>
public sealed class DuplicantInventoryStorer
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantPathFollower pathFollower;
    private readonly DuplicantStorageReservation storageReservation;

    public InventoryStoreResult Result { get; private set; }

    public DuplicantInventoryStorer(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantPathFollower pathFollower,
        DuplicantStorageReservation storageReservation)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.pathFollower = pathFollower;
        this.storageReservation = storageReservation;
    }

    public IEnumerator StoreCarriedInventory()
    {
        Result = InventoryStoreResult.NothingToStore;

        if (inventory == null
            || !inventory.HasItem
            || !inventory.CarriedType.HasValue)
        {
            yield break;
        }

        ResourceType type = inventory.CarriedType.Value;
        int amount = inventory.CarriedAmount;

        if (StructureManager.Instance == null
            || !StructureManager.Instance
                .TryReserveReachableStorage(
                    type,
                    amount,
                    controller.gridPosition,
                    out IStorage storage))
        {
            Result = InventoryStoreResult.StorageUnavailable;
            yield break;
        }

        if (!storageReservation.Track(
                storage,
                type,
                amount))
        {
            Result =
                InventoryStoreResult.ReservationTrackingFailed;

            yield break;
        }

        var path = TaskNavigationUtility
            .GetPathToInteractionPosition(
                controller.gridPosition,
                storageReservation.StoragePosition,
                true,
                controller.capabilityProfile);

        if (path == null)
        {
            Result = InventoryStoreResult.PathUnavailable;
            yield break;
        }

        yield return pathFollower.FollowInteraction(
            storageReservation.StoragePosition,
            path);

        if (!pathFollower.Succeeded)
        {
            Result = InventoryStoreResult.PathUnavailable;
            yield break;
        }

        if (!storageReservation.TryStore(inventory))
        {
            Result = InventoryStoreResult.StorageRejected;
            yield break;
        }

        storageReservation.Complete();
        inventory.Clear();

        Result = InventoryStoreResult.Stored;
    }
}