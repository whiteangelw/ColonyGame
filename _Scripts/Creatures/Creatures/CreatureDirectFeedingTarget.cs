using System;
using UnityEngine;

[RequireComponent(typeof(CreatureController), typeof(CreatureDomestication))]
public sealed class CreatureDirectFeedingTarget : MonoBehaviour,
    IResourceDeliveryTarget
{
    public event Action StateChanged;
    private CreatureController controller;
    private CreatureDomestication domestication;
    private ResourceType? requestedFood;
    private int incomingReservation;

    public bool HasPendingRequest => requestedFood.HasValue;
    public ResourceType? RequestedFood => requestedFood;
    public bool DomesticationCanReceiveOffering => domestication != null
        && domestication.CanReceiveOffering(out _);
    public string LastRequestResult { get; private set; } = "Nenhum pedido";
    public Vector2Int GridPosition => controller != null
        ? controller.GridPosition
        : Vector2Int.zero;
    public bool IsDeliveryTargetValid => isActiveAndEnabled
        && controller != null
        && controller.IsInitialized
        && requestedFood.HasValue;

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
        domestication = GetComponent<CreatureDomestication>();
    }

    private void OnDisable()
    {
        incomingReservation = 0;
        requestedFood = null;
        TaskManager.Instance?.CancelTasksForDeliveryTarget(this);
    }

    public bool RequestFeeding(ResourceType foodType)
    {
        if (!domestication.CanReceiveOffering(out string reason))
        {
            LastRequestResult = reason;
            StateChanged?.Invoke();
            return false;
        }

        // Substitui uma ordem anterior sem emitir o estado intermediário
        // "cancelado" para a UI que acabou de solicitar a nova oferta.
        incomingReservation = 0;
        requestedFood = null;
        TaskManager.Instance?.CancelTasksForDeliveryTarget(this);
        requestedFood = foodType;
        incomingReservation = 0;
        controller.Movement?.CancelMovement();
        controller.Feeding?.CancelCurrentFeeding();
        ReevaluateTasks();
        controller.Brain?.ForceDecision();
        LastRequestResult = "Pedido criado: " + foodType;
        StateChanged?.Invoke();
        return true;
    }

    public void CancelRequest()
    {
        if (!requestedFood.HasValue && incomingReservation <= 0) return;

        incomingReservation = 0;
        requestedFood = null;
        TaskManager.Instance?.CancelTasksForDeliveryTarget(this);
        controller?.Brain?.ForceDecision();
        LastRequestResult = "Pedido cancelado";
        StateChanged?.Invoke();
    }

    public bool NeedsInput(ResourceType type)
    {
        return IsDeliveryTargetValid
            && requestedFood == type
            && incomingReservation == 0
            && domestication.CanReceiveOffering(out _);
    }

    public bool HasOutstandingInput(ResourceType type)
        => IsDeliveryTargetValid && requestedFood == type;

    public bool TryReserveInput(
        ResourceType type,
        int capacity,
        out int reservedAmount)
    {
        reservedAmount = 0;
        if (!NeedsInput(type) || capacity <= 0
            || !domestication.CanReceiveOffering(out _)) return false;

        incomingReservation = 1;
        reservedAmount = 1;
        StateChanged?.Invoke();
        return true;
    }

    public void ReleaseInputReservation(ResourceType type, int amount)
    {
        if (requestedFood != type || amount <= 0) return;
        incomingReservation = Mathf.Max(0, incomingReservation - amount);
        ReevaluateTasks();
        StateChanged?.Invoke();
    }

    public int AcceptReservedInput(ResourceType type, int amount)
    {
        if (requestedFood != type || incomingReservation <= 0 || amount <= 0)
            return 0;

        DirectFeedingOutcome outcome = domestication.RegisterDirectFeeding(type);
        string creatureId = controller.Definition != null
            ? controller.Definition.creatureId
            : string.Empty;
        GameEvents.TriggerCreatureFeedingObserved(creatureId, type, outcome);
        incomingReservation = 0;
        requestedFood = null;
        if (outcome == DirectFeedingOutcome.Refused
            || outcome == DirectFeedingOutcome.InvalidFood)
        {
            LastRequestResult = outcome == DirectFeedingOutcome.InvalidFood
                ? "A criatura recusou a oferta; recurso devolvido"
                : "Entrega recusada; recurso devolvido";
            StateChanged?.Invoke();
            return 0;
        }

        bool uncomfortable = outcome == DirectFeedingOutcome.AcceptedOverLimit;
        controller.Feeding?.ReceiveDirectMeal(type, uncomfortable);
        LastRequestResult = uncomfortable
            ? "Aceitou em excesso e ficou desconfortável"
            : "Alimentação direta concluída";
        StateChanged?.Invoke();
        return 1;
    }

    public void ReevaluateTasks()
    {
        if (!requestedFood.HasValue || incomingReservation > 0) return;

        if (!domestication.CanReceiveOffering(out string reason))
        {
            LastRequestResult = reason;
            return;
        }
        TaskManager.Instance?.AddResourceDeliveryTask(this, requestedFood.Value);
    }
}
