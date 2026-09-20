using UnityEngine;

[RequireComponent(typeof(CreatureController), typeof(CreatureDomestication))]
public sealed class CreatureDirectFeedingTarget : MonoBehaviour,
    IResourceDeliveryTarget
{
    private CreatureController controller;
    private CreatureDomestication domestication;
    private ResourceType? requestedFood;
    private int incomingReservation;

    public bool HasPendingRequest => requestedFood.HasValue;
    public ResourceType? RequestedFood => requestedFood;
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
        CreatureTamingProfileSO profile = controller.Definition != null
            ? controller.Definition.tamingProfile
            : null;
        if (profile == null || !profile.TryGetRule(foodType, out _))
        {
            LastRequestResult = "Alimento não configurado para esta espécie";
            return false;
        }
        if (!domestication.CanAcceptDirectFeeding(foodType, out string reason))
        {
            LastRequestResult = reason;
            return false;
        }

        CancelRequest();
        requestedFood = foodType;
        incomingReservation = 0;
        controller.Movement?.CancelMovement();
        controller.Feeding?.CancelCurrentFeeding();
        ReevaluateTasks();
        controller.Brain?.ForceDecision();
        LastRequestResult = "Pedido criado: " + foodType;
        return true;
    }

    public void CancelRequest()
    {
        incomingReservation = 0;
        requestedFood = null;
        TaskManager.Instance?.CancelTasksForDeliveryTarget(this);
        controller?.Brain?.ForceDecision();
        LastRequestResult = "Pedido cancelado";
    }

    public bool NeedsInput(ResourceType type)
    {
        return IsDeliveryTargetValid
            && requestedFood == type
            && incomingReservation == 0
            && domestication.CanAcceptDirectFeeding(type, out _);
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
            || !domestication.CanAcceptDirectFeeding(type, out _)) return false;

        incomingReservation = 1;
        reservedAmount = 1;
        return true;
    }

    public void ReleaseInputReservation(ResourceType type, int amount)
    {
        if (requestedFood != type || amount <= 0) return;
        incomingReservation = Mathf.Max(0, incomingReservation - amount);
        ReevaluateTasks();
    }

    public int AcceptReservedInput(ResourceType type, int amount)
    {
        if (requestedFood != type || incomingReservation <= 0 || amount <= 0)
            return 0;

        DirectFeedingOutcome outcome = domestication.RegisterDirectFeeding(type);
        incomingReservation = 0;
        requestedFood = null;
        if (outcome == DirectFeedingOutcome.Refused
            || outcome == DirectFeedingOutcome.InvalidFood)
        {
            LastRequestResult = "Entrega recusada; recurso devolvido";
            return 0;
        }

        bool uncomfortable = outcome == DirectFeedingOutcome.AcceptedOverLimit;
        controller.Feeding?.ReceiveDirectMeal(type, uncomfortable);
        LastRequestResult = uncomfortable
            ? "Aceitou em excesso e ficou desconfortável"
            : "Alimentação direta concluída";
        return 1;
    }

    public void ReevaluateTasks()
    {
        if (!requestedFood.HasValue || incomingReservation > 0) return;

        if (!domestication.CanAcceptDirectFeeding(
            requestedFood.Value, out string reason))
        {
            LastRequestResult = reason;
            return;
        }
        TaskManager.Instance?.AddResourceDeliveryTask(this, requestedFood.Value);
    }
}
