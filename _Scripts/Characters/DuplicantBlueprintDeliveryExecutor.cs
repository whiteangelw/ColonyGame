using System.Collections;
using System.Collections.Generic;
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
    private const int MaximumContinuationPlans = 12;
    private int maximumStopsForCurrentExecution;
    private int visitedStopsThisExecution;
    private readonly HashSet<Task> rejectedTasks =
        new HashSet<Task>();

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
        int defaultMaximumStops = TaskManager.Instance != null
            ? TaskManager.Instance.MaximumBlueprintDeliveriesPerRun
            : 1;

        yield return ExecuteInternal(
            task,
            true,
            defaultMaximumStops);
    }

    /// <summary>
    /// Entrega o recurso que já está no inventário, sem buscá-lo novamente.
    /// </summary>
    public IEnumerator ExecuteCarried(Task task, int maximumStops)
    {
        yield return ExecuteInternal(
            task,
            false,
            maximumStops);

        // Este modo roda dentro de outra tarefa. Não pode deixar reservas
        // temporárias aguardando o encerramento do Runner.
        deliveryPlan.ReleaseAll();
    }

    private IEnumerator ExecuteInternal(
        Task task,
        bool fetchResource,
        int maximumStops)
    {
        Result = BlueprintDeliveryExecutionResult.None;
        DeliveredAmount = 0;
        rejectedTasks.Clear();
        maximumStopsForCurrentExecution = Mathf.Max(1, maximumStops);
        visitedStopsThisExecution = 0;

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

        if (fetchResource)
        {
            int plannedAmount =
                deliveryPlan.ReservedDeliveryAmount;

            yield return resourceCollector.Fetch(
                requiredResource,
                plannedAmount);
        }

        if (!inventory.HasItem
            || inventory.CarriedType != requiredResource)
        {
            Result =
                BlueprintDeliveryExecutionResult.ResourceUnavailable;

            yield break;
        }

        deliveryPlan.TrimToCollectedAmount(
            inventory.CarriedAmount);

        int continuationPlans = 0;

        while (inventory.HasItem
               && inventory.CarriedType == requiredResource)
        {
            yield return DeliverCurrentPlan(requiredResource);

            deliveryPlan.RemoveResolvedStops();

            if (!inventory.HasItem)
            {
                break;
            }

            if (continuationPlans >= MaximumContinuationPlans)
            {
                break;
            }

            continuationPlans++;

            if (!TryBuildContinuationPlan(requiredResource))
            {
                break;
            }
        }

        Result = DeliveredAmount > 0
            ? BlueprintDeliveryExecutionResult.Completed
            : BlueprintDeliveryExecutionResult
                .NoDestinationReceivedLoad;
    }

    private IEnumerator DeliverCurrentPlan(
    ResourceType requiredResource)
    {
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

            visitedStopsThisExecution++;

            ConstructionBlueprint blueprint = stop.Blueprint;

            if (blueprint == null
                || blueprint.CurrentState
                    != BlueprintState.WaitingMaterials)
            {
                RejectStop(stop);
                continue;
            }

            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToTask(
                    controller.gridPosition,
                    stop.Task,
                    controller.capabilityProfile);

            if (path == null)
            {
                RejectStop(stop);
                continue;
            }

            yield return pathFollower.FollowTask(
                stop.Task,
                path);

            if (!pathFollower.Succeeded || blueprint == null)
            {
                RejectStop(stop);
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
                RejectStop(stop);
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
    }

    private void RejectStop(BlueprintDeliveryStop stop)
    {
        if (stop == null)
        {
            return;
        }

        if (stop.Task != null)
        {
            rejectedTasks.Add(stop.Task);
        }

        deliveryPlan.ReleaseStop(stop);
    }

    private bool TryBuildContinuationPlan(
        ResourceType resourceType)
    {
        if (!inventory.HasItem
            || inventory.CarriedType != resourceType
            || inventory.CarriedAmount <= 0
            || TaskManager.Instance == null)
        {
            return false;
        }

        int cargoRemaining = inventory.CarriedAmount;

        int maximumStops = maximumStopsForCurrentExecution;

        while (cargoRemaining > 0
               && visitedStopsThisExecution + deliveryPlan.Count
                    < maximumStops)
        {
            if (!TaskManager.Instance
                    .TryAssignAdditionalBlueprintDeliveryTask(
                        controller.gridPosition,
                        controller.gridPosition,
                        resourceType,
                        controller.capabilityProfile,
                        out Task additionalTask,
                        rejectedTasks))
            {
                break;
            }

            if (!deliveryPlan.TryAddStop(
                    additionalTask,
                    additionalTask.targetBlueprint,
                    cargoRemaining,
                    resourceType))
            {
                rejectedTasks.Add(additionalTask);

                TaskManager.Instance.ReleaseTask(
                    additionalTask);

                continue;
            }

            cargoRemaining = Mathf.Max(
                0,
                inventory.CarriedAmount
                    - deliveryPlan.ReservedDeliveryAmount);
        }

        return deliveryPlan.ReservedDeliveryAmount > 0;
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

        int deliveryCapacity =
            GetDeliveryCapacity(resourceType);

        int capacityRemaining = deliveryCapacity;

        if (capacityRemaining <= 0
            || !deliveryPlan.TryAddStop(
                primaryTask,
                primaryBlueprint,
                capacityRemaining,
                resourceType))
        {
            return false;
        }

        capacityRemaining = deliveryCapacity
            - deliveryPlan.ReservedDeliveryAmount;

        Vector2Int batchAnchor =
            primaryBlueprint.gridPosition;

        int maximumStops = maximumStopsForCurrentExecution;

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
                        out Task additionalTask,
                        rejectedTasks))
            {
                break;
            }

            if (!deliveryPlan.TryAddStop(
                    additionalTask,
                    additionalTask.targetBlueprint,
                    capacityRemaining,
                    resourceType))
            {
                rejectedTasks.Add(additionalTask);

                TaskManager.Instance.ReleaseTask(
                    additionalTask);

                continue;
            }

            capacityRemaining = inventory.SpaceRemaining
                - deliveryPlan.ReservedDeliveryAmount;
        }

        return deliveryPlan.ReservedDeliveryAmount > 0;
    }
    private int GetDeliveryCapacity(
    ResourceType resourceType)
    {
        if (!inventory.HasItem)
        {
            return inventory.SpaceRemaining;
        }

        if (inventory.CarriedType != resourceType)
        {
            return 0;
        }

        // Se já está carregando o recurso correto,
        // primeiro tenta entregar a carga atual.
        return inventory.CarriedAmount;
    }
}
