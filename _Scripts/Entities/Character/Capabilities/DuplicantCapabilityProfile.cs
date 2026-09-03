using UnityEngine;

[CreateAssetMenu(
    fileName = "NewCapabilityProfile",
    menuName = "Pathfinding/Capability Profile"
)]
public class DuplicantCapabilityProfile : ScriptableObject
{
    [Header("Limites Físicos (Blocos)")]

    [Tooltip(
        "Altura máxima em blocos que o personagem consegue subir sem escada."
    )]
    [Range(1, 3)]
    public int maxStepUp = 2;

    [Tooltip(
        "Distância máxima horizontal que o personagem consegue saltar sobre buracos."
    )]
    [Range(0, 3)]
    public int maxJumpGap = 2;

    [Tooltip(
        "Profundidade máxima de queda segura em blocos."
    )]
    [Range(1, 5)]
    public int maxDropHeight = 3;

    [Header("Alcance de Interação")]

    [Tooltip(
        "Alcance de interação a partir do bloco dos pés."
    )]
    [Range(1, 6)]
    public int buildAndDigRange = 4;

    [Header("Pesos / Custos Base de Movimento")]

    [Min(0.01f)]
    public float baseWalkCost = 1f;

    [Min(0.01f)]
    public float stepUpCostMultiplier = 1.5f;

    [Min(0.01f)]
    public float jumpCostMultiplier = 2.0f;

    [Min(0.01f)]
    public float ladderCostMultiplier = 0.8f;

    [Header("Permissões de Terreno")]

    public bool canUseLadders = true;
    public bool canClimbWalls = true;

    public bool CanUseMovement(
        MovementType moveType,
        Vector2Int from,
        Vector2Int to)
    {
        int deltaX = Mathf.Abs(to.x - from.x);
        int deltaY = to.y - from.y;

        switch (moveType)
        {
            case MovementType.Walk:
                return deltaY == 0 && deltaX == 1;

            case MovementType.StepUp:
                return canClimbWalls
                    && deltaX == 1
                    && deltaY > 0
                    && deltaY <= Mathf.Min(maxStepUp, 2);

            case MovementType.StepDown:
                return deltaX == 1
                    && deltaY < 0
                    && -deltaY <= maxDropHeight;

            case MovementType.JumpGap:
                return deltaY == 0
                    && Mathf.Max(0, deltaX - 1) <= maxJumpGap;

            case MovementType.ClimbLadder:
                return canUseLadders && deltaX == 0 && Mathf.Abs(deltaY) == 1;

            default:
                return false;
        }
    }

    public float GetEdgeMovementCost(
        MovementType moveType,
        Vector2Int from,
        Vector2Int to)
    {
        int horizontalDistance = Mathf.Abs(to.x - from.x);
        int verticalDistance = Mathf.Abs(to.y - from.y);
        float distance;

        switch (moveType)
        {
            case MovementType.StepUp:
                distance = 1f + verticalDistance * 0.5f;
                break;

            case MovementType.StepDown:
                distance = 1f + verticalDistance * 0.2f;
                break;

            case MovementType.JumpGap:
                distance = horizontalDistance;
                break;

            default:
                distance = Mathf.Max(1f, horizontalDistance + verticalDistance);
                break;
        }

        return GetMovementCost(moveType, distance);
    }

    public float GetMinimumCostPerGridUnit()
    {
        float minimumMultiplier = 1f;

        int safeDropHeight = Mathf.Max(1, maxDropHeight);
        float stepDownCostPerGridUnit =
            (1f + safeDropHeight * 0.2f)
            / (1f + safeDropHeight);

        minimumMultiplier = Mathf.Min(
            minimumMultiplier,
            stepDownCostPerGridUnit
        );

        if (canUseLadders)
        {
            minimumMultiplier = Mathf.Min(
                minimumMultiplier,
                Mathf.Max(0.01f, ladderCostMultiplier)
            );
        }

        return Mathf.Max(0.01f, baseWalkCost) * minimumMultiplier;
    }

    public float GetMovementCost(
        MovementType moveType,
        float distance = 1f)
    {
        distance = Mathf.Max(0.01f, distance);

        switch (moveType)
        {
            case MovementType.Walk:
                return baseWalkCost * distance;

            case MovementType.StepUp:
                return baseWalkCost
                    * distance
                    * stepUpCostMultiplier;

            case MovementType.StepDown:
                return baseWalkCost * distance;

            case MovementType.JumpGap:
                return baseWalkCost
                    * distance
                    * jumpCostMultiplier;

            case MovementType.ClimbLadder:
                if (!canUseLadders)
                {
                    return float.MaxValue;
                }

                return baseWalkCost
                    * distance
                    * ladderCostMultiplier;

            default:
                return baseWalkCost * distance;
        }
    }
}
