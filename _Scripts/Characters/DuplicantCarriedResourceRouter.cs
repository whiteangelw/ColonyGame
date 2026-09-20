using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CarriedResourceRouteResult
{
    KeptStorageRoute,
    DeliveredToBlueprints,
    DeliveredToMachine
}

/// <summary>
/// Decide se uma carga já coletada deve ir primeiro para máquinas ou blueprints.
/// Não coleta recursos e não armazena itens.
/// </summary>
public sealed class DuplicantCarriedResourceRouter
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantBlueprintDeliveryExecutor deliveryExecutor;
    private readonly ProductionDeliveryExecutor productionDeliveryExecutor;
    private readonly DuplicantStorageReservation storageReservation;
    private readonly DuplicantLogisticsRoutingSettings settings;

    public CarriedResourceRouteResult Result { get; private set; }
    public int DeliveredAmount { get; private set; }

    public DuplicantCarriedResourceRouter(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantBlueprintDeliveryExecutor deliveryExecutor,
        ProductionDeliveryExecutor productionDeliveryExecutor,
        DuplicantStorageReservation storageReservation,
        DuplicantLogisticsRoutingSettings settings)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.deliveryExecutor = deliveryExecutor;
        this.productionDeliveryExecutor = productionDeliveryExecutor;
        this.storageReservation = storageReservation;
        this.settings = settings;
    }

    public IEnumerator TryDeliverToDemandTargets(Task groundHaulTask)
    {
        Result = CarriedResourceRouteResult.KeptStorageRoute;
        DeliveredAmount = 0;

        if (!CanConsiderDirectDelivery()
            || TaskManager.Instance == null)
        {
            yield break;
        }

        ResourceType resourceType = inventory.CarriedType.Value;

        yield return TryDeliverToMachine(resourceType, groundHaulTask);

        if (!inventory.HasItem)
        {
            yield break;
        }

        resourceType = inventory.CarriedType.Value;

        if (!TaskManager.Instance.TryAssignAdditionalBlueprintDeliveryTask(
                controller.gridPosition,
                controller.gridPosition,
                resourceType,
                controller.capabilityProfile,
                out Task deliveryTask,
                null,
                settings.MaximumDirectDeliveryDistance))
        {
            Log("Nenhuma blueprint compatível e alcançável.");
            yield break;
        }

        if (!ShouldUseBlueprintRoute(groundHaulTask, deliveryTask))
        {
            TaskManager.Instance.ReleaseTask(deliveryTask);
            yield break;
        }

        yield return deliveryExecutor.ExecuteCarried(
            deliveryTask,
            settings.MaximumBlueprintStops);

        int blueprintDelivered = deliveryExecutor.DeliveredAmount;

        if (blueprintDelivered <= 0)
        {
            TaskManager.Instance.ReleaseTask(deliveryTask);
            Log("A rota direta não conseguiu entregar; mantendo o baú.");
            yield break;
        }

        storageReservation.ReleaseAmount(blueprintDelivered);
        DeliveredAmount += blueprintDelivered;
        Result = CarriedResourceRouteResult.DeliveredToBlueprints;
        Log($"Entregou {blueprintDelivered}x {resourceType} diretamente para blueprint.");
    }

    private IEnumerator TryDeliverToMachine(
        ResourceType resourceType,
        Task groundHaulTask)
    {
        if (productionDeliveryExecutor == null
            || TaskManager.Instance == null
            || !TaskManager.Instance.TryAssignMachineSupplyTask(
                controller.gridPosition,
                resourceType,
                controller.capabilityProfile,
                settings.MaximumDirectDeliveryDistance,
                out Task machineTask))
        {
            Log("Nenhuma máquina compatível e alcançável.");
            yield break;
        }

        IResourceDeliveryTarget deliveryTarget =
            machineTask.targetResourceDelivery
            ?? machineTask.targetProductionMachine;

        if (!ShouldUseMachineRoute(groundHaulTask, machineTask))
        {
            TaskManager.Instance.ReleaseTask(machineTask);
            yield break;
        }

        yield return productionDeliveryExecutor.ExecuteCarried(machineTask);

        int delivered = productionDeliveryExecutor.DeliveredAmount;
        if (delivered <= 0)
        {
            TaskManager.Instance.ReleaseTask(machineTask);
            Log("A entrega direta para máquina não pôde ser concluída.");
            yield break;
        }

        storageReservation.ReleaseAmount(delivered);
        DeliveredAmount += delivered;
        Result = CarriedResourceRouteResult.DeliveredToMachine;
        TaskManager.Instance.RemoveTask(machineTask);
        deliveryTarget?.ReevaluateTasks();
        Log($"Entregou {delivered}x {resourceType} diretamente ao destino.");
    }

    private bool ShouldUseMachineRoute(
        Task groundHaulTask,
        Task machineTask)
    {
        if (machineTask == null) return false;

        if (settings.requireEqualOrHigherPriority
            && groundHaulTask != null
            && machineTask.priority < groundHaulTask.priority)
        {
            Log("Máquina recusada por ter prioridade menor que o transporte.");
            return false;
        }

        List<Vector2Int> machinePath = TaskNavigationUtility.GetPathToTask(
            controller.gridPosition,
            machineTask,
            controller.capabilityProfile);
        if (machinePath == null) return false;

        List<Vector2Int> storagePath =
            TaskNavigationUtility.GetPathToInteractionPosition(
                controller.gridPosition,
                storageReservation.StoragePosition,
                true,
                controller.capabilityProfile);

        return storagePath == null
            || machinePath.Count
                <= storagePath.Count + settings.AllowedExtraTravelSteps;
    }

    private bool CanConsiderDirectDelivery()
    {
        return settings != null
            && settings.enableDirectBlueprintDelivery
            && controller != null
            && inventory != null
            && inventory.HasItem
            && inventory.CarriedType.HasValue
            && inventory.CarriedAmount >= settings.MinimumCarriedAmount
            && storageReservation != null
            && storageReservation.IsActive;
    }

    private bool ShouldUseBlueprintRoute(
        Task groundHaulTask,
        Task deliveryTask)
    {
        if (deliveryTask == null)
        {
            return false;
        }

        if (settings.requireEqualOrHigherPriority
            && groundHaulTask != null
            && deliveryTask.priority < groundHaulTask.priority)
        {
            Log("Blueprint recusada por ter prioridade menor.");
            return false;
        }

        List<Vector2Int> blueprintPath =
            TaskNavigationUtility.GetPathToTask(
                controller.gridPosition,
                deliveryTask,
                controller.capabilityProfile);

        if (blueprintPath == null)
        {
            Log("Blueprint recusada por não possuir caminho.");
            return false;
        }

        List<Vector2Int> storagePath =
            TaskNavigationUtility.GetPathToInteractionPosition(
                controller.gridPosition,
                storageReservation.StoragePosition,
                true,
                controller.capabilityProfile);

        if (storagePath == null)
        {
            return true;
        }

        bool withinTravelBudget = blueprintPath.Count
            <= storagePath.Count + settings.AllowedExtraTravelSteps;

        if (!withinTravelBudget)
        {
            Log(
                $"Baú mantido: blueprint={blueprintPath.Count} passos, "
                + $"baú={storagePath.Count} passos.");
        }

        return withinTravelBudget;
    }

    private void Log(string message)
    {
        if (settings != null && settings.enableDiagnostics)
        {
            Debug.Log(
                $"[LogisticsRouting] {controller.name}; "
                + $"Posição={controller.gridPosition}; {message}");
        }
    }
}
