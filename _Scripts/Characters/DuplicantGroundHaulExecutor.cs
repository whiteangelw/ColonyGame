using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public enum GroundHaulExecutionResult
{
    None,
    Completed,
    InvalidTarget,
    ItemReservedByOther,
    StorageUnavailable,
    StorageReservationFailed,
    ItemUnreachable,
    ItemUnavailable,
    InventoryRejected,
    StorageUnreachableAfterPickup,
    StorageRejected
}

/// <summary>
/// Executa a coleta de recursos do chão e a entrega em um baú.
/// Não decide como reportar falhas ou encerrar a tarefa.
/// </summary>
public sealed class DuplicantGroundHaulExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantPathFollower pathFollower;
    private readonly DuplicantStorageReservation storageReservation;
    private readonly DuplicantGroundHaulState groundHaulState;
    private readonly DuplicantTaskRunner runner;
    private readonly Func<Vector3> getSafeDropPosition;

    public GroundHaulExecutionResult Result { get; private set; }

    public DuplicantGroundHaulExecutor(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantPathFollower pathFollower,
        DuplicantStorageReservation storageReservation,
        DuplicantGroundHaulState groundHaulState,
        DuplicantTaskRunner runner,
        Func<Vector3> getSafeDropPosition)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.pathFollower = pathFollower;
        this.storageReservation = storageReservation;
        this.groundHaulState = groundHaulState;
        this.runner = runner;
        this.getSafeDropPosition = getSafeDropPosition;
    }

    public IEnumerator Execute(
        Task task,
        List<Vector2Int> initialPath)
    {
        Result = GroundHaulExecutionResult.None;

        ResourceItem item = task.targetItem;
        groundHaulState.SetActiveItem(item);

        if (item == null
            || !item.gameObject.activeInHierarchy
            || !item.IsReadyForHaul
            || item.amount <= 0
            || StructureManager.Instance == null)
        {
            Result = GroundHaulExecutionResult.InvalidTarget;
            yield break;
        }

        if (!groundHaulState.TryReserveItem(item, runner))
        {
            Result =
                GroundHaulExecutionResult.ItemReservedByOther;

            yield break;
        }

        if (inventory.HasItem
            && inventory.CarriedType != item.type)
        {
            inventory.DropCarriedItem(
                getSafeDropPosition());
        }

        int pickupAmount = Mathf.Min(
            item.amount,
            inventory.SpaceRemaining);

        int amountToStore =
            inventory.CarriedAmount + pickupAmount;

        if (amountToStore <= 0
            || !StructureManager.Instance
                .TryReserveReachableStorage(
                    item.type,
                    amountToStore,
                    GridManager.Instance.WorldToGridPosition(
                        item.transform.position),
                    out IStorage storage))
        {
            Result = GroundHaulExecutionResult.StorageUnavailable;
            yield break;
        }

        if (!storageReservation.Track(
                storage,
                item.type,
                amountToStore))
        {
            Result =
                GroundHaulExecutionResult
                    .StorageReservationFailed;

            yield break;
        }

        Vector2Int itemPosition =
            GridManager.Instance.WorldToGridPosition(
                item.transform.position);

        List<Vector2Int> pathToItem = initialPath;

        if (pathToItem == null
            || task.gridPosition != itemPosition)
        {
            pathToItem = PathfindingAStar.Instance?.FindPath(
                controller.gridPosition,
                itemPosition,
                controller.capabilityProfile);

            task.gridPosition = itemPosition;
        }

        yield return pathFollower.FollowPosition(
            itemPosition,
            pathToItem);

        if (!pathFollower.Succeeded
            || item == null
            || !item.gameObject.activeInHierarchy)
        {
            Result = GroundHaulExecutionResult.ItemUnreachable;
            yield break;
        }

        int requestedPickup = Mathf.Min(
            pickupAmount,
            inventory.SpaceRemaining);

        if (!item.TryTake(
                requestedPickup,
                out int takenAmount))
        {
            Result = GroundHaulExecutionResult.ItemUnavailable;
            yield break;
        }

        groundHaulState.ReleaseItemReservation(runner);

        int acceptedPickup = inventory.AddItem(
            item.type,
            takenAmount);

        if (acceptedPickup < takenAmount)
        {
            item.ReturnAmount(takenAmount - acceptedPickup);
        }

        if (acceptedPickup <= 0)
        {
            Result = GroundHaulExecutionResult.InventoryRejected;
            yield break;
        }

        groundHaulState.MarkItemCollected();
        task.MarkGroundHaulCarrying(runner);

        int unusedReservation =
            storageReservation.ReservedAmount
            - inventory.CarriedAmount;

        if (unusedReservation > 0)
        {
            storageReservation.ReleaseAmount(
                unusedReservation);
        }

        if (item.amount <= 0)
        {
            item.Recycle();
        }

        yield return CollectAdditionalItems(item.type);

        List<Vector2Int> pathToStorage =
            TaskNavigationUtility
                .GetPathToInteractionPosition(
                    controller.gridPosition,
                    storageReservation.StoragePosition,
                    true,
                    controller.capabilityProfile);

        if (pathToStorage == null)
        {
            Result =
                GroundHaulExecutionResult
                    .StorageUnreachableAfterPickup;

            yield break;
        }

        yield return pathFollower.FollowInteraction(
            storageReservation.StoragePosition,
            pathToStorage);

        if (!pathFollower.Succeeded)
        {
            Result =
                GroundHaulExecutionResult
                    .StorageUnreachableAfterPickup;

            yield break;
        }

        if (!storageReservation.TryStore(inventory))
        {
            Result = GroundHaulExecutionResult.StorageRejected;
            yield break;
        }

        storageReservation.Complete();
        inventory.Clear();

        TaskManager.Instance?.RemoveTask(task);

        if (item != null
            && item.gameObject.activeInHierarchy
            && item.amount > 0)
        {
            TaskManager.Instance?.AddHaulTask(item);
        }

        Result = GroundHaulExecutionResult.Completed;
    }

    private IEnumerator CollectAdditionalItems(ResourceType type)
    {
        while (inventory.SpaceRemaining > 0)
        {
            if (TaskManager.Instance == null
                || !TaskManager.Instance
                    .TryAssignAdditionalGroundHaulTask(
                        controller.gridPosition,
                        type,
                        controller.capabilityProfile,
                        out Task additionalTask,
                        out List<Vector2Int> path))
            {
                yield break;
            }

            ResourceItem item = additionalTask.targetItem;

            groundHaulState.TrackAdditionalTask(additionalTask);

            if (item == null
                || !item.gameObject.activeInHierarchy
                || !item.IsReadyForHaul
                || item.type != type
                || item.amount <= 0)
            {
                groundHaulState.ReleaseAdditionalTask(true);
                continue;
            }

            if (!groundHaulState.TryReserveItem(item, runner))
            {
                groundHaulState.ReleaseAdditionalTask();
                yield break;
            }

            Vector2Int itemPosition =
                GridManager.Instance.WorldToGridPosition(
                    item.transform.position);

            yield return pathFollower.FollowPosition(
                itemPosition,
                path);

            if (!pathFollower.Succeeded
                || item == null
                || !item.gameObject.activeInHierarchy)
            {
                groundHaulState.ReleaseItemReservation(runner);
                groundHaulState.ReleaseAdditionalTask();
                yield break;
            }

            int amountToTake = Mathf.Min(
                item.amount,
                inventory.SpaceRemaining);

            if (!storageReservation.TryExpand(amountToTake))
            {
                groundHaulState.ReleaseItemReservation(runner);
                groundHaulState.ReleaseAdditionalTask();
                yield break;
            }

            if (!item.TryTake(
                    amountToTake,
                    out int takenAmount))
            {
                storageReservation.ReleaseAmount(
                    amountToTake);

                groundHaulState.ReleaseItemReservation(runner);
                groundHaulState.ReleaseAdditionalTask();
                yield break;
            }

            int acceptedAmount = inventory.AddItem(
                type,
                takenAmount);

            int rejectedAmount =
                takenAmount - acceptedAmount;

            if (rejectedAmount > 0)
            {
                item.ReturnAmount(rejectedAmount);

                storageReservation.ReleaseAmount(
                    rejectedAmount);
            }

            groundHaulState.ReleaseItemReservation(runner);

            if (item.amount <= 0)
            {
                item.Recycle();
                groundHaulState.ReleaseAdditionalTask(true);
            }
            else
            {
                groundHaulState.ReleaseAdditionalTask();
            }
        }
    }
}