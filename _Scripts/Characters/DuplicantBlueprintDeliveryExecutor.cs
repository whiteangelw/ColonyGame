using System.Collections;
using UnityEngine;

public enum BlueprintDeliveryExecutionResult
{
    None,
    PrimaryBlueprintInvalid,
    NoMaterialPlan,
    ResourceUnavailable,
    NoDestinationReceivedLoad,
    Completed
}

/// <summary>
/// Executa a coleta e entrega de materiais para blueprints.
/// Não decide o que fazer após sucesso, falha ou adiamento.
/// </summary>
public sealed class DuplicantBlueprintDeliveryExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantPathFollower pathFollower;
    private readonly DuplicantResourceCollector resourceCollector;
    private readonly DuplicantBlueprintDeliveryPlan deliveryPlan;

    public BlueprintDeliveryExecutionResult Result { get; private set; }

    public int DeliveredAmount { get; private set; }

    public DuplicantBlueprintDeliveryExecutor(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantPathFollower pathFollower,
        DuplicantResourceCollector resourceCollector,
        DuplicantBlueprintDeliveryPlan deliveryPlan)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.pathFollower = pathFollower;
        this.resourceCollector = resourceCollector;
        this.deliveryPlan = deliveryPlan;
    }

    public IEnumerator Execute(Task task)
    {
        Result = BlueprintDeliveryExecutionResult.None;
        DeliveredAmount = 0;

        ConstructionBlueprint primaryBlueprint =
            task.targetBlueprint;

        if (primaryBlueprint == null
            || primaryBlueprint.CurrentState
                != BlueprintState.WaitingMaterials)
        {
            Result =
                BlueprintDeliveryExecutionResult.PrimaryBlueprintInvalid;

            yield break;
        }

        ResourceType requiredResource =
            primaryBlueprint.requiredResource;

        if (!TryBuildPlan(
                task,
                primaryBlueprint,
                requiredResource))
        {
            Result =
                BlueprintDeliveryExecutionResult.NoMaterialPlan;

            yield break;
        }

        int plannedAmount =
            deliveryPlan.ReservedDeliveryAmount;

        yield return resourceCollector.Fetch(
            requiredResource,
            plannedAmount);

        if (!inventory.HasItem)
        {
            Result =
                BlueprintDeliveryExecutionResult.ResourceUnavailable;

            yield break;
        }

        deliveryPlan.TrimToCollectedAmount(
            inventory.CarriedAmount);

        for (int i = 0;
             i < deliveryPlan.Count && inventory.HasItem;
             i++)
        {
            BlueprintDeliveryStop stop =
                deliveryPlan.GetStop(i);

            if (stop == null || stop.ReservedAmount <= 0)
            {
                continue;
            }

            ConstructionBlueprint blueprint = stop.Blueprint;

            if (blueprint == null
                || blueprint.CurrentState
                    != BlueprintState.WaitingMaterials)
            {
                deliveryPlan.ReleaseStop(stop);
                continue;
            }

            var path = TaskNavigationUtility.GetPathToTask(
                controller.gridPosition,
                stop.Task,
                controller.capabilityProfile);

            if (path == null)
            {
                deliveryPlan.ReleaseStop(stop);
                continue;
            }

            yield return pathFollower.FollowTask(
                stop.Task,
                path);

            if (!pathFollower.Succeeded || blueprint == null)
            {
                deliveryPlan.ReleaseStop(stop);
                continue;
            }

            int requestedAmount = Mathf.Min(
                stop.ReservedAmount,
                inventory.CarriedAmount);

            int deliveredAmount = blueprint.DeliverResource(
                requiredResource,
                requestedAmount);

            if (deliveredAmount <= 0)
            {
                deliveryPlan.ReleaseStop(stop);
                continue;
            }

            int confirmedAmount =
                deliveryPlan.ConfirmDelivery(
                    stop,
                    deliveredAmount);

            inventory.RemoveItem(
                requiredResource,
                confirmedAmount);

            DeliveredAmount += confirmedAmount;

            if (stop.ReservedAmount > 0)
            {
                deliveryPlan.ReleaseStop(stop);
            }

            TaskManager.Instance?.RemoveTask(stop.Task);
            blueprint.NotifyTaskEnded(stop.Task);
        }

        Result = DeliveredAmount > 0
            ? BlueprintDeliveryExecutionResult.Completed
            : BlueprintDeliveryExecutionResult
                .NoDestinationReceivedLoad;
    }

    private bool TryBuildPlan(
        Task primaryTask,
        ConstructionBlueprint primaryBlueprint,
        ResourceType resourceType)
    {
        if (StockpileManager.Instance == null
            || TaskManager.Instance == null)
        {
            return false;
        }

        int capacityRemaining = inventory.SpaceRemaining;

        if (capacityRemaining <= 0
            || !deliveryPlan.TryAddStop(
                primaryTask,
                primaryBlueprint,
                capacityRemaining,
                resourceType))
        {
            return false;
        }

        capacityRemaining = inventory.SpaceRemaining
            - deliveryPlan.ReservedDeliveryAmount;

        Vector2Int batchAnchor =
            primaryBlueprint.gridPosition;

        int maximumStops =
            TaskManager.Instance
                .MaximumBlueprintDeliveriesPerRun;

        while (capacityRemaining > 0
               && deliveryPlan.Count < maximumStops
               && StockpileManager.Instance
                   .GetAvailableAmount(resourceType) > 0)
        {
            if (!TaskManager.Instance
                    .TryAssignAdditionalBlueprintDeliveryTask(
                        controller.gridPosition,
                        batchAnchor,
                        resourceType,
                        controller.capabilityProfile,
                        out Task additionalTask))
            {
                break;
            }

            if (!deliveryPlan.TryAddStop(
                    additionalTask,
                    additionalTask.targetBlueprint,
                    capacityRemaining,
                    resourceType))
            {
                TaskManager.Instance.ReleaseTask(
                    additionalTask);

                break;
            }

            capacityRemaining = inventory.SpaceRemaining
                - deliveryPlan.ReservedDeliveryAmount;
        }

        return deliveryPlan.ReservedDeliveryAmount > 0;
    }
}