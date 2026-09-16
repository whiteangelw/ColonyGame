using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mantém um conjunto de entregas de um mesmo recurso para blueprints.
/// Garante que reservas de blueprint e de estoque sejam liberadas juntas.
/// </summary>
public sealed class DuplicantBlueprintDeliveryPlan
{
    private readonly List<BlueprintDeliveryStop> stops =
        new List<BlueprintDeliveryStop>(12);

    private ResourceType resourceType;
    private int reservedDeliveryAmount;
    private int reservedResourceAmount;

    public int Count => stops.Count;

    public ResourceType ResourceType => resourceType;

    public int ReservedDeliveryAmount =>
        reservedDeliveryAmount;

    public int ReservedResourceAmount =>
        reservedResourceAmount;

    public ConstructionBlueprint PrimaryBlueprint =>
        stops.Count > 0
            ? stops[0].Blueprint
            : null;

    public BlueprintDeliveryStop GetStop(int index)
    {
        return index >= 0 && index < stops.Count
            ? stops[index]
            : null;
    }

    /// <summary>
    /// Reserva um destino e o material necessário para ele.
    /// </summary>
    public bool TryAddStop(
        Task task,
        ConstructionBlueprint blueprint,
        int capacityRemaining,
        ResourceType requiredResource)
    {
        if (task == null
            || blueprint == null
            || blueprint.CurrentState
                != BlueprintState.WaitingMaterials
            || blueprint.requiredResource != requiredResource
            || capacityRemaining <= 0
            || StockpileManager.Instance == null)
        {
            return false;
        }

        if (stops.Count > 0
            && resourceType != requiredResource)
        {
            return false;
        }

        int requestedAmount = Mathf.Min(
            capacityRemaining,
            Mathf.Min(
                blueprint.GetRemainingNeededAmount(),
                StockpileManager.Instance.GetAvailableAmount(
                    requiredResource)));

        if (requestedAmount <= 0)
        {
            return false;
        }

        int blueprintReservedAmount =
            blueprint.ReserveDelivery(requestedAmount);

        if (blueprintReservedAmount <= 0)
        {
            return false;
        }

        if (!StockpileManager.Instance.ReserveResource(
                requiredResource,
                blueprintReservedAmount))
        {
            blueprint.CancelDeliveryReservation(
                blueprintReservedAmount);

            return false;
        }

        resourceType = requiredResource;

        stops.Add(new BlueprintDeliveryStop(
            task,
            blueprint,
            blueprintReservedAmount));

        reservedDeliveryAmount += blueprintReservedAmount;
        reservedResourceAmount += blueprintReservedAmount;

        return true;
    }

    /// <summary>
    /// Ajusta as reservas caso o duplicant tenha coletado menos que o planejado.
    /// </summary>
    public void TrimToCollectedAmount(int collectedAmount)
    {
        int excessReservation = Mathf.Max(
            0,
            reservedDeliveryAmount - collectedAmount);

        for (int i = stops.Count - 1;
             i >= 0 && excessReservation > 0;
             i--)
        {
            BlueprintDeliveryStop stop = stops[i];

            int amountToRelease = Mathf.Min(
                stop.ReservedAmount,
                excessReservation);

            ReleaseStopAmount(stop, amountToRelease);

            excessReservation -= amountToRelease;
        }

        RemoveEmptyStops();
    }

    /// <summary>
    /// Registra material entregue com sucesso.
    /// O commit no Stockpile já foi feito pela blueprint.
    /// </summary>
    public int ConfirmDelivery(
        BlueprintDeliveryStop stop,
        int deliveredAmount)
    {
        if (stop == null || deliveredAmount <= 0)
        {
            return 0;
        }

        int confirmedAmount = stop.ConfirmDelivery(
            deliveredAmount);

        reservedDeliveryAmount = Mathf.Max(
            0,
            reservedDeliveryAmount - confirmedAmount);

        reservedResourceAmount = Mathf.Max(
            0,
            reservedResourceAmount - confirmedAmount);

        return confirmedAmount;
    }

    /// <summary>
    /// Libera um destino inteiro que não poderá receber a carga.
    /// </summary>
    public void ReleaseStop(BlueprintDeliveryStop stop)
    {
        if (stop == null)
        {
            return;
        }

        ReleaseStopAmount(
            stop,
            stop.ReservedAmount);

        TaskManager.Instance?.ReleaseTask(stop.Task);
    }

    /// <summary>
    /// Libera todas as reservas ainda pendentes.
    /// </summary>
    public void ReleaseAll()
    {
        for (int i = stops.Count - 1; i >= 0; i--)
        {
            ReleaseStop(stops[i]);
        }

        stops.Clear();
        reservedDeliveryAmount = 0;
        reservedResourceAmount = 0;
    }

    private void ReleaseStopAmount(
        BlueprintDeliveryStop stop,
        int requestedAmount)
    {
        if (stop == null || requestedAmount <= 0)
        {
            return;
        }

        int releasedAmount = stop.RemoveReservation(
            requestedAmount);

        if (releasedAmount <= 0)
        {
            return;
        }

        if (stop.Blueprint != null)
        {
            stop.Blueprint.CancelDeliveryReservation(
                releasedAmount);
        }

        StockpileManager.Instance?.UnreserveResource(
            resourceType,
            releasedAmount);

        reservedDeliveryAmount = Mathf.Max(
            0,
            reservedDeliveryAmount - releasedAmount);

        reservedResourceAmount = Mathf.Max(
            0,
            reservedResourceAmount - releasedAmount);
    }

    private void RemoveEmptyStops()
    {
        for (int i = stops.Count - 1; i >= 0; i--)
        {
            BlueprintDeliveryStop stop = stops[i];

            if (stop.ReservedAmount > 0)
            {
                continue;
            }

            TaskManager.Instance?.ReleaseTask(stop.Task);
            stops.RemoveAt(i);
        }
    }
}