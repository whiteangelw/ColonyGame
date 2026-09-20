using UnityEngine;

/// <summary>Destino que recebe recursos através da logística dos colonos.</summary>
public interface IResourceDeliveryTarget
{
    Vector2Int GridPosition { get; }
    bool NeedsInput(ResourceType type);
    bool HasOutstandingInput(ResourceType type);
    bool TryReserveInput(ResourceType type, int capacity, out int reservedAmount);
    void ReleaseInputReservation(ResourceType type, int amount);
    int AcceptReservedInput(ResourceType type, int amount);
    void ReevaluateTasks();
    bool IsDeliveryTargetValid { get; }
}
