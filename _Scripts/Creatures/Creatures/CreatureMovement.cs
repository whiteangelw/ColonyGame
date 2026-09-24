using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CreatureController))]
public sealed class CreatureMovement : MonoBehaviour
{
    private CreatureController controller;
    private Coroutine movementRoutine;
    private GridManager subscribedGrid;
    private bool pathValidationRequested;
    public bool IsMoving => movementRoutine != null;
    public int RemainingSteps { get; private set; }
    public string LastFailure { get; private set; } = "Nenhuma";

    private void Awake() => controller = GetComponent<CreatureController>();

    private void OnEnable()
    {
        TrySubscribeToGrid();
    }

    private void OnDisable()
    {
        CancelMovement();
        UnsubscribeFromGrid();
    }

    public bool TryMoveTo(Vector2Int target)
    {
        TrySubscribeToGrid();
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
        pathValidationRequested = false;
        int firstIndex = path.Count > 0 && path[0] == controller.GridPosition ? 1 : 0;

        for (int i = firstIndex; i < path.Count; i++)
        {
            Vector2Int nextPosition = path[i];
            if (!CanStillTraverse(controller.GridPosition, nextPosition))
            {
                StopBecauseWorldChanged(nextPosition);
                yield break;
            }

            RemainingSteps = path.Count - i;
            Vector3 destination = GridManager.Instance.GridToWorldPosition(nextPosition);
            while ((transform.position - destination).sqrMagnitude > 0.0004f)
            {
                if (pathValidationRequested)
                {
                    pathValidationRequested = false;
                    if (!CanStillTraverse(controller.GridPosition, nextPosition))
                    {
                        StopBecauseWorldChanged(nextPosition);
                        yield break;
                    }
                }

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

    private bool CanStillTraverse(Vector2Int from, Vector2Int to)
    {
        return NavGraphGenerator.Instance != null
            && NavGraphGenerator.Instance.CanTraverse(
                from,
                to,
                controller.Definition.navigationProfile);
    }

    private void StopBecauseWorldChanged(Vector2Int blockedStep)
    {
        // Volta à última célula confirmada. Isso impede que a criatura fique
        // visualmente no meio de um bloco criado durante o deslocamento.
        if (GridManager.Instance != null)
        {
            transform.position = GridManager.Instance.GridToWorldPosition(
                controller.GridPosition);
        }

        LastFailure = "Caminho alterado durante o movimento em " + blockedStep;
        RemainingSteps = 0;
        movementRoutine = null;
        controller.Brain?.ForceDecision();
    }

    private void TrySubscribeToGrid()
    {
        GridManager current = GridManager.Instance;
        if (current == null || current == subscribedGrid) return;

        UnsubscribeFromGrid();
        subscribedGrid = current;
        subscribedGrid.OnTileChanged += HandleTileChanged;
        subscribedGrid.OnGridRebuilt += HandleGridRebuilt;
    }

    private void UnsubscribeFromGrid()
    {
        if (subscribedGrid == null) return;
        subscribedGrid.OnTileChanged -= HandleTileChanged;
        subscribedGrid.OnGridRebuilt -= HandleGridRebuilt;
        subscribedGrid = null;
    }

    private void HandleTileChanged(int x, int y, TileType newType)
    {
        if (IsMoving) pathValidationRequested = true;
    }

    private void HandleGridRebuilt()
    {
        if (IsMoving) pathValidationRequested = true;
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
