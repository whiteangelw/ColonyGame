using UnityEngine;

[CreateAssetMenu(fileName = "CreatureNavigationProfile",
    menuName = "Creatures/Navigation Profile")]
public sealed class CreatureNavigationProfileSO : ScriptableObject, INavigationProfile
{
    [Range(1, 2)] public int maxStepUp = 1;
    [Range(0, 3)] public int maxJumpGap;
    [Range(1, 5)] public int maxDropHeight = 2;
    public bool canUseLadders;
    [Min(0.01f)] public float walkCost = 1f;
    [Min(0.01f)] public float climbCost = 1.5f;
    [Min(0.01f)] public float jumpCost = 2f;

    public bool CanUseMovement(MovementType type, Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dy = to.y - from.y;
        switch (type)
        {
            case MovementType.Walk: return dx == 1 && dy == 0;
            case MovementType.StepUp: return dx == 1 && dy > 0 && dy <= maxStepUp;
            case MovementType.StepDown: return dx == 1 && dy < 0 && -dy <= maxDropHeight;
            case MovementType.JumpGap: return dy == 0 && Mathf.Max(0, dx - 1) <= maxJumpGap;
            case MovementType.ClimbLadder: return canUseLadders && dx == 0 && Mathf.Abs(dy) == 1;
            default: return false;
        }
    }

    public float GetEdgeMovementCost(MovementType type, Vector2Int from, Vector2Int to)
    {
        float distance = Mathf.Max(1, Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y));
        if (type == MovementType.JumpGap) return walkCost * distance * jumpCost;
        if (type == MovementType.StepUp || type == MovementType.ClimbLadder)
            return walkCost * distance * climbCost;
        return walkCost * distance;
    }

    public float GetMinimumCostPerGridUnit() => Mathf.Max(0.01f, walkCost);
}
