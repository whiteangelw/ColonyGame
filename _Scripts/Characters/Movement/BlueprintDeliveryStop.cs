using UnityEngine;

/// <summary>
/// Representa uma entrega reservada para uma blueprint específica.
/// </summary>
public sealed class BlueprintDeliveryStop
{
    public Task Task { get; }
    public ConstructionBlueprint Blueprint { get; }
    public int ReservedAmount { get; private set; }

    public bool IsValid =>
        Task != null
        && Blueprint != null
        && Blueprint.CurrentState
            == BlueprintState.WaitingMaterials
        && ReservedAmount > 0;

    public BlueprintDeliveryStop(
        Task task,
        ConstructionBlueprint blueprint,
        int reservedAmount)
    {
        Task = task;
        Blueprint = blueprint;
        ReservedAmount = Mathf.Max(
            0,
            reservedAmount);
    }

    /// <summary>
    /// Confirma que uma parte da reserva foi entregue.
    /// </summary>
    public int ConfirmDelivery(int deliveredAmount)
    {
        int confirmedAmount = Mathf.Clamp(
            deliveredAmount,
            0,
            ReservedAmount);

        ReservedAmount -= confirmedAmount;
        return confirmedAmount;
    }

    /// <summary>
    /// Retira uma quantidade da reserva para que seja liberada.
    /// </summary>
    public int RemoveReservation(int requestedAmount)
    {
        int removedAmount = Mathf.Clamp(
            requestedAmount,
            0,
            ReservedAmount);

        ReservedAmount -= removedAmount;
        return removedAmount;
    }
}