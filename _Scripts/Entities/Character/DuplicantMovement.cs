using System.Collections;
using UnityEngine;

[RequireComponent(typeof(DuplicantController))]
public class DuplicantMovement : MonoBehaviour
{
    private DuplicantController controller;

    public bool LastMoveSucceeded { get; private set; }

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
    }

    /// <summary>
    /// Move o colono suavemente até o nó de destino no grid.
    /// </summary>
    public IEnumerator MoveToTile(
        Vector2Int targetGridPos,
        MovementType moveType,
        float speed,
        bool validateNavigation = true)
    {
        LastMoveSucceeded = false;

        if (GridManager.Instance == null)
        {
            yield break;
        }

        Vector2Int startGridPos = controller.gridPosition;
        bool isFreeFall = !validateNavigation
            && moveType == MovementType.StepDown;

        if (validateNavigation && !CanContinueMove(startGridPos, targetGridPos))
        {
            yield break;
        }

        if (isFreeFall && !CanContinueFall(targetGridPos))
        {
            yield break;
        }

        float cs = GridManager.Instance.cellSize;
        Vector3 startWorldPos = transform.position;

        Vector3 targetWorldPos = new Vector3(
            targetGridPos.x * cs + cs / 2f,
            targetGridPos.y * cs,
            0
        );

        float elapsed = 0f;
        float duration = 1f / Mathf.Max(speed, 0.1f);

        if (moveType == MovementType.JumpGap)
        {
            while (elapsed < duration)
            {
                if (validateNavigation && !CanContinueMove(startGridPos, targetGridPos))
                {
                    transform.position = startWorldPos;
                    yield break;
                }

                if (isFreeFall && !CanContinueFall(targetGridPos))
                {
                    transform.position = startWorldPos;
                    yield break;
                }

                float t = elapsed / duration;
                Vector3 currentPos = Vector3.Lerp(startWorldPos, targetWorldPos, t);

                float jumpArc = Mathf.Sin(t * Mathf.PI) * 0.4f;
                currentPos.y += jumpArc;

                transform.position = currentPos;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            while (elapsed < duration)
            {
                if (validateNavigation && !CanContinueMove(startGridPos, targetGridPos))
                {
                    transform.position = startWorldPos;
                    yield break;
                }

                if (isFreeFall && !CanContinueFall(targetGridPos))
                {
                    transform.position = startWorldPos;
                    yield break;
                }

                transform.position = Vector3.Lerp(startWorldPos, targetWorldPos, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        transform.position = targetWorldPos;
        controller.gridPosition = targetGridPos;
        LastMoveSucceeded = true;
    }

    private bool CanContinueMove(Vector2Int from, Vector2Int to)
    {
        return NavGraphGenerator.Instance != null
            && NavGraphGenerator.Instance.CanTraverse(from, to);
    }

    private bool CanContinueFall(Vector2Int target)
    {
        return GridManager.Instance != null
            && GridManager.Instance.IsPassable(target.x, target.y);
    }

    /// <summary>
    /// Rotina de queda livre contínua caso o chão abaixo seja minerado.
    /// </summary>
    public IEnumerator HandleFallingRoutine()
    {
        controller.currentState = DuplicantController.WorkerState.Falling;

        while (ShouldFall() && controller.gridPosition.y > 0)
        {
            Vector2Int fallTargetPos = new Vector2Int(controller.gridPosition.x, controller.gridPosition.y - 1);
            yield return MoveToTile(
                fallTargetPos,
                MovementType.StepDown,
                controller.fallSpeed,
                false
            );

            if (!LastMoveSucceeded)
            {
                break;
            }
        }

        controller.currentState = DuplicantController.WorkerState.Idle;
    }

    /// <summary>
    /// Dedução do tipo de movimento com base no deslocamento.
    /// </summary>
    public MovementType DeduceMovementType(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;

        if (GridManager.Instance != null && GridManager.Instance.IsLadder(from.x, from.y) && GridManager.Instance.IsLadder(to.x, to.y))
            return MovementType.ClimbLadder;

        if (Mathf.Abs(dx) > 1 && dy == 0) return MovementType.JumpGap;
        if (dy > 0) return MovementType.StepUp;
        if (dy < 0) return MovementType.StepDown;

        return MovementType.Walk;
    }

    /// <summary>
    /// Verifica se o colono perdeu sustentação sob os pés.
    /// </summary>
    public bool ShouldFall()
    {
        if (GridManager.Instance == null) return false;

        if (controller.gridPosition.y <= 0)
        {
            return false;
        }

        if (GridManager.Instance.IsLadder(
                controller.gridPosition.x,
                controller.gridPosition.y))
        {
            return false;
        }

        bool hasSolidSupport = GridManager.Instance.IsSolid(
            controller.gridPosition.x,
            controller.gridPosition.y - 1
        );

        bool hasLadderSupport = GridManager.Instance.IsLadder(
            controller.gridPosition.x,
            controller.gridPosition.y - 1
        );

        return !hasSolidSupport && !hasLadderSupport;
    }

    /// <summary>
    /// Calcula a velocidade ajustada para o movimento, terreno e líquidos.
    /// </summary>
    public float GetCurrentMoveSpeed(MovementType moveType)
    {
        float speed = controller.baseMoveSpeed;

        if (controller.capabilityProfile != null)
        {
            float costMultiplier = controller.capabilityProfile.GetMovementCost(moveType, 1f);
            speed /= Mathf.Max(costMultiplier, 0.1f);
        }

        if (GridManager.Instance != null)
        {
            Tile currentTile = GridManager.Instance.GetTile(controller.gridPosition.x, controller.gridPosition.y);
            if (currentTile != null && currentTile.liquidAmount > 0.3f)
            {
                speed *= 0.5f;
            }
        }

        return speed;
    }

    /// <summary>
    /// Alinha instantaneamente o transform do colono ao grid.
    /// </summary>
    public void SnapToGrid(Vector2Int gPos)
    {
        if (GridManager.Instance == null) return;
        float cs = GridManager.Instance.cellSize;

        transform.position = new Vector3(
            gPos.x * cs + cs / 2f,
            gPos.y * cs,
            0
        );
        controller.gridPosition = gPos;
    }
}
