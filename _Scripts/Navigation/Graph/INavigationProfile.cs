using UnityEngine;

public interface INavigationProfile
{
    bool CanUseMovement(MovementType moveType, Vector2Int from, Vector2Int to);
    float GetEdgeMovementCost(MovementType moveType, Vector2Int from, Vector2Int to);
    float GetMinimumCostPerGridUnit();
}
