using UnityEngine;

public interface IFoodSource
{
    Vector2Int GridPosition { get; }
    bool IsRawFood { get; }
    bool HasFood { get; }
    bool TryReservePortion(DuplicantController duplicant);
    bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored);
    void ReleaseReservation(DuplicantController duplicant);
}
