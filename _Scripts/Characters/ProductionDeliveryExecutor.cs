using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ProductionDeliveryResult
{
    None,
    Completed,
    InvalidTarget,
    NothingReserved,
    ResourceUnavailable,
    DestinationUnreachable,
    DestinationRejected
}

public sealed class ProductionDeliveryExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantPathFollower pathFollower;
    private readonly DuplicantResourceCollector resourceCollector;

    private IResourceDeliveryTarget reservedTarget;
    private ResourceType reservedType;
    private int reservedAmount;
    private int stockpileReservedAmount;

    public ProductionDeliveryResult Result { get; private set; }
    public int DeliveredAmount { get; private set; }

    public ProductionDeliveryExecutor(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantPathFollower pathFollower,
        DuplicantResourceCollector resourceCollector)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.pathFollower = pathFollower;
        this.resourceCollector = resourceCollector;
    }

    public IEnumerator Execute(Task task)
    {
        yield return ExecuteInternal(task, true);
    }

    public IEnumerator ExecuteCarried(Task task)
    {
        yield return ExecuteInternal(task, false);
    }

    private IEnumerator ExecuteInternal(Task task, bool fetchResource)
    {
        Result = ProductionDeliveryResult.None;
        DeliveredAmount = 0;
        IResourceDeliveryTarget target = task?.targetResourceDelivery
            ?? task?.targetProductionMachine;
        if (target == null || !target.IsDeliveryTargetValid
            || !task.requestedResourceType.HasValue)
        {
            Result = ProductionDeliveryResult.InvalidTarget;
            yield break;
        }

        ResourceType type = task.requestedResourceType.Value;
        if (inventory.HasItem && inventory.CarriedType != type)
        {
            Result = ProductionDeliveryResult.ResourceUnavailable;
            yield break;
        }

        int carryingSameType = inventory.CarriedType == type
            ? inventory.CarriedAmount
            : 0;
        int carryingCapacity = fetchResource
            ? carryingSameType + inventory.SpaceRemaining
            : carryingSameType;
        if (!target.TryReserveInput(type, carryingCapacity, out reservedAmount))
        {
            Result = ProductionDeliveryResult.NothingReserved;
            yield break;
        }

        reservedTarget = target;
        reservedType = type;

        if (fetchResource
            && (StockpileManager.Instance == null
                || !StockpileManager.Instance.ReserveResource(type, reservedAmount)))
        {
            ReleaseReservation();
            Result = ProductionDeliveryResult.ResourceUnavailable;
            yield break;
        }
        stockpileReservedAmount = fetchResource ? reservedAmount : 0;

        if (fetchResource)
        {
            yield return resourceCollector.Fetch(type, reservedAmount);
        }

        int deliverable = inventory.CarriedType == type
            ? Mathf.Min(inventory.CarriedAmount, reservedAmount)
            : 0;
        if (deliverable <= 0)
        {
            ReleaseReservation();
            Result = ProductionDeliveryResult.ResourceUnavailable;
            yield break;
        }


        if (deliverable < stockpileReservedAmount)
        {
            int excess = stockpileReservedAmount - deliverable;
            StockpileManager.Instance?.UnreserveResource(type, excess);
            stockpileReservedAmount -= excess;
            target.ReleaseInputReservation(type, excess);
            reservedAmount -= excess;
        }

        List<Vector2Int> path = TaskNavigationUtility.GetPathToInteractionPosition(
            controller.gridPosition,
            target.GridPosition);
        if (path == null)
        {
            ReleaseReservation();
            Result = ProductionDeliveryResult.DestinationUnreachable;
            yield break;
        }

        yield return pathFollower.FollowInteraction(target.GridPosition, path);
        if (!pathFollower.Succeeded || !target.IsDeliveryTargetValid)
        {
            ReleaseReservation();
            Result = ProductionDeliveryResult.DestinationUnreachable;
            yield break;
        }

        if (stockpileReservedAmount > 0
            && (StockpileManager.Instance == null
                || !StockpileManager.Instance.CommitReservedResource(
                    type,
                    deliverable)))
        {
            ReleaseReservation();
            Result = ProductionDeliveryResult.DestinationRejected;
            yield break;
        }
        stockpileReservedAmount = Mathf.Max(0, stockpileReservedAmount - deliverable);

        int removed = inventory.RemoveItem(type, deliverable);
        int accepted = target.AcceptReservedInput(type, removed);
        DeliveredAmount = accepted;
        int rejected = removed - accepted;
        if (rejected > 0) inventory.AddItem(type, rejected);

        if (accepted < reservedAmount)
        {
            target.ReleaseInputReservation(type, reservedAmount - accepted);
        }

        ClearReservationTracking();
        Result = accepted > 0
            ? ProductionDeliveryResult.Completed
            : ProductionDeliveryResult.DestinationRejected;
    }

    public void Cancel()
    {
        ReleaseReservation();
    }

    private void ReleaseReservation()
    {
        if (reservedTarget != null && reservedAmount > 0)
        {
            reservedTarget.ReleaseInputReservation(reservedType, reservedAmount);
        }
        if (stockpileReservedAmount > 0)
        {
            StockpileManager.Instance?.UnreserveResource(
                reservedType,
                stockpileReservedAmount);
        }
        ClearReservationTracking();
    }

    private void ClearReservationTracking()
    {
        reservedTarget = null;
        reservedAmount = 0;
        stockpileReservedAmount = 0;
    }
}
