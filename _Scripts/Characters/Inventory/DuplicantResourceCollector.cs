using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coleta um tipo de recurso em armazenamentos ou no chão.
/// Não cria tarefas, não reserva blueprints e não decide o destino da carga.
/// </summary>
public sealed class DuplicantResourceCollector
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantPathFollower pathFollower;
    private readonly DuplicantTaskRunner reservationOwner;
    private ResourceItem reservedGroundItem;

    private readonly List<ResourceItem> activeItemsBuffer =
        new List<ResourceItem>();

    public DuplicantResourceCollector(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantPathFollower pathFollower,
        DuplicantTaskRunner reservationOwner)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.pathFollower = pathFollower;
        this.reservationOwner = reservationOwner;
    }

    public void Cancel()
    {
        ReleaseGroundItemReservation();
    }

    public IEnumerator Fetch(
        ResourceType type,
        int targetAmount)
    {
        if (targetAmount <= 0)
        {
            yield break;
        }

        if (inventory.HasItem
            && inventory.CarriedType != type)
        {
            yield break;
        }

        int remainingAmount = Mathf.Max(
            0,
            targetAmount - inventory.CarriedAmount);

        if (remainingAmount <= 0)
        {
            yield break;
        }

        yield return FetchFromStorages(
            type,
            remainingAmount);

        remainingAmount = Mathf.Max(
            0,
            targetAmount - inventory.CarriedAmount);

        if (remainingAmount <= 0
            || inventory.SpaceRemaining <= 0)
        {
            yield break;
        }

        yield return FetchFromGround(
            type,
            remainingAmount);
    }

    private IEnumerator FetchFromStorages(
        ResourceType type,
        int remainingAmount)
    {
        if (StructureManager.Instance == null)
        {
            yield break;
        }

        List<StorageStructure> storages =
            StructureManager.Instance.GetStoragesWithResource(
                type);

        foreach (StorageStructure storage in storages)
        {
            if (remainingAmount <= 0
                || inventory.SpaceRemaining <= 0)
            {
                yield break;
            }

            List<Vector2Int> storagePath =
                TaskNavigationUtility
                    .GetPathToInteractionPosition(
                        controller.gridPosition,
                        storage.GridPosition);

            if (storagePath == null)
            {
                continue;
            }

            yield return pathFollower.FollowInteraction(
                storage.GridPosition,
                storagePath);

            if (!pathFollower.Succeeded
                || storage == null)
            {
                continue;
            }

            int requestedAmount = Mathf.Min(
                remainingAmount,
                inventory.SpaceRemaining,
                storage.GetLocalAmount(type));

            if (requestedAmount <= 0
                || !storage.WithdrawItem(
                    type,
                    requestedAmount))
            {
                continue;
            }

            int acceptedAmount = inventory.AddItem(
                type,
                requestedAmount);

            int rejectedAmount =
                requestedAmount - acceptedAmount;

            if (rejectedAmount > 0)
            {
                storage.StoreItem(
                    type,
                    rejectedAmount);
            }

            remainingAmount -= acceptedAmount;
        }
    }

    private IEnumerator FetchFromGround(
        ResourceType type,
        int remainingAmount)
    {
        ResourceItem.CopyActiveItemsTo(activeItemsBuffer);

        foreach (ResourceItem item in activeItemsBuffer)
        {
            if (remainingAmount <= 0
                || inventory.SpaceRemaining <= 0)
            {
                yield break;
            }

            if (item == null
                || !item.gameObject.activeInHierarchy
                || item.type != type
                || item.amount <= 0)
            {
                continue;
            }

            Vector2Int itemGridPosition =
                GridManager.Instance.WorldToGridPosition(
                    item.transform.position);

            List<Vector2Int> path =
                PathfindingAStar.Instance?.FindPath(
                    controller.gridPosition,
                    itemGridPosition);

            if (path == null)
            {
                continue;
            }

            if (reservationOwner == null
                || !item.TryReserve(reservationOwner))
            {
                continue;
            }

            reservedGroundItem = item;

            yield return pathFollower.FollowPosition(
                itemGridPosition,
                path);

            if (!pathFollower.Succeeded
                || item == null
                || !item.gameObject.activeInHierarchy)
            {
                ReleaseGroundItemReservation();
                continue;
            }

            int requestedAmount = Mathf.Min(
                remainingAmount,
                inventory.SpaceRemaining);

            if (!item.TryTake(
                    requestedAmount,
                    out int takenAmount))
            {
                ReleaseGroundItemReservation();
                continue;
            }

            ReleaseGroundItemReservation();

            int acceptedAmount = inventory.AddItem(
                type,
                takenAmount);

            int rejectedAmount =
                takenAmount - acceptedAmount;

            if (rejectedAmount > 0)
            {
                item.ReturnAmount(rejectedAmount);
            }

            remainingAmount -= acceptedAmount;

            if (item.amount <= 0)
            {
                item.Recycle();
            }
        }
    }

    private void ReleaseGroundItemReservation()
    {
        if (reservedGroundItem != null && reservationOwner != null)
        {
            reservedGroundItem.ReleaseReservation(reservationOwner);
        }
        reservedGroundItem = null;
    }
}
