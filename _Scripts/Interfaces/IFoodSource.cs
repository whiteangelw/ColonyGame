using UnityEngine;

public interface IFoodSource
{
    Vector2Int GridPosition { get; }
    bool IsEmergencyOnly { get; }
    bool HasFood { get; }
    bool TryReservePortion(DuplicantController duplicant);
    bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood);
    void ReleaseReservation(DuplicantController duplicant);
}
