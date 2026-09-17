using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CarriedResourceRouteResult
{
    KeptStorageRoute,
    DeliveredToBlueprints
}

/// <summary>
/// Decide se uma carga já coletada deve ir primeiro para blueprints.
/// Não coleta recursos e não armazena itens.
/// </summary>
public sealed class DuplicantCarriedResourceRouter
{
    private readonly DuplicantController controller;
    private readonly DuplicantInventory inventory;
    private readonly DuplicantBlueprintDeliveryExecutor deliveryExecutor;
    private readonly DuplicantStorageReservation storageReservation;
    private readonly DuplicantLogisticsRoutingSettings settings;

    public CarriedResourceRouteResult Result { get; private set; }
    public int DeliveredAmount { get; private set; }

    public DuplicantCarriedResourceRouter(
        DuplicantController controller,
        DuplicantInventory inventory,
        DuplicantBlueprintDeliveryExecutor deliveryExecutor,
        DuplicantStorageReservation storageReservation,
        DuplicantLogisticsRoutingSettings settings)
    {
        this.controller = controller;
        this.inventory = inventory;
        this.deliveryExecutor = deliveryExecutor;
        this.storageReservation = storageReservation;
        this.settings = settings;
    }

    public IEnumerator TryDeliverToBlueprints(Task groundHaulTask)
    {
        Result = CarriedResourceRouteResult.KeptStorageRoute;
        DeliveredAmount = 0;

        if (!CanConsiderDirectDelivery()
            || TaskManager.Instance == null)
        {
            yield break;
        }

        ResourceType resourceType = inventory.CarriedType.Value;

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

        DeliveredAmount = deliveryExecutor.DeliveredAmount;

        if (DeliveredAmount <= 0)
        {
            TaskManager.Instance.ReleaseTask(deliveryTask);
            Log("A rota direta não conseguiu entregar; mantendo o baú.");
            yield break;
        }

        storageReservation.ReleaseAmount(DeliveredAmount);
        Result = CarriedResourceRouteResult.DeliveredToBlueprints;
        Log($"Entregou {DeliveredAmount}x {resourceType} diretamente.");
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
