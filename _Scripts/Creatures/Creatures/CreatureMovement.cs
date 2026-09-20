using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CreatureController))]
public sealed class CreatureMovement : MonoBehaviour
{
    private CreatureController controller;
    private Coroutine movementRoutine;
    public bool IsMoving => movementRoutine != null;
    public int RemainingSteps { get; private set; }
    public string LastFailure { get; private set; } = "Nenhuma";

    private void Awake() => controller = GetComponent<CreatureController>();

    public bool TryMoveTo(Vector2Int target)
    {
        if (IsMoving || controller.Definition == null || PathfindingAStar.Instance == null)
            return false;

        List<Vector2Int> path = PathfindingAStar.Instance.FindPath(
            controller.GridPosition, target, controller.Definition.navigationProfile);
        if (path == null)
        {
            LastFailure = "Nenhum caminho até " + target;
            return false;
        }

        movementRoutine = StartCoroutine(FollowPath(path));
        return true;
    }

    public void CancelMovement()
    {
        if (movementRoutine != null) StopCoroutine(movementRoutine);
        movementRoutine = null;
        RemainingSteps = 0;
    }

    private IEnumerator FollowPath(List<Vector2Int> path)
    {
        LastFailure = "Nenhuma";
        int firstIndex = path.Count > 0 && path[0] == controller.GridPosition ? 1 : 0;

        for (int i = firstIndex; i < path.Count; i++)
        {
            RemainingSteps = path.Count - i;
            Vector3 destination = GridManager.Instance.GridToWorldPosition(path[i]);
            while ((transform.position - destination).sqrMagnitude > 0.0004f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    destination,
                    GetCurrentMoveSpeed() * Time.deltaTime);
                yield return null;
            }
            transform.position = destination;
            controller.RefreshGridPosition();
        }

        RemainingSteps = 0;
        movementRoutine = null;
    }

    private float GetCurrentMoveSpeed()
    {
        float baseSpeed = controller.Definition != null
            ? controller.Definition.moveSpeed
            : 1f;
        if (controller.State == CreatureState.Avoid)
            return Mathf.Max(0.1f, baseSpeed * 1.25f);

        float modifier = controller.Domestication != null
            ? controller.Domestication.GetMovementSpeedMultiplier()
            : 1f;
        return Mathf.Max(0.1f, baseSpeed * modifier);
    }
}
