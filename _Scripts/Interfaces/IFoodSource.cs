using UnityEngine;
using System.Collections.Generic;

public interface IFoodSource
{
    Vector2Int GridPosition { get; }
    FoodSourceKind SourceKind { get; }
    bool IsEmergencyOnly { get; }
    bool HasFood { get; }
    void CollectFoodOptions(List<FoodOption> results);
    bool TryReserveMeal(
        DuplicantController duplicant,
        ResourceType foodType,
        int requestedPortions,
        out int reservedPortions);
    int GetReservedPortionCount(DuplicantController duplicant);
    bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood);
    void ReleaseReservation(DuplicantController duplicant);
}
