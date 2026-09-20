using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executa deslocamentos e recálculos de caminho.
/// Não escolhe tarefas e não controla inventário ou reservas.
/// </summary>
public sealed class DuplicantPathFollower
{
    private const int MaximumReplans = 8;

    private readonly DuplicantController controller;
    private readonly DuplicantMovement movement;
    private readonly DuplicantTaskDiagnostics diagnostics;

    private readonly List<Vector2Int> emptyPath =
        new List<Vector2Int>(0);

    public bool Succeeded { get; private set; }

    public DuplicantPathFollower(
        DuplicantController controller,
        DuplicantMovement movement,
        DuplicantTaskDiagnostics diagnostics)
    {
        this.controller = controller;
        this.movement = movement;
        this.diagnostics = diagnostics;
    }

    public IEnumerator FollowTask(
        Task task,
        List<Vector2Int> initialPath)
    {
        Succeeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;

        diagnostics.BeginPath(
            task != null
                ? task.gridPosition
                : controller.gridPosition,
            currentPath,
            MaximumReplans);

        while (true)
        {
            diagnostics.UpdatePath(
                currentPath,
                pathIndex,
                replanCount);

            if (task == null
                || TaskManager.Instance == null
                || !TaskManager.Instance.ContainsTask(task))
            {
                diagnostics.RecordFailure(
                    TaskFailureReason.Interrupted,
                    "A tarefa foi cancelada durante o deslocamento.");

                yield break;
            }

            if (currentPath == null)
            {
                diagnostics.RecordFailure(
                    TaskFailureReason.TargetUnreachable,
                    "Não existe caminho até a tarefa.");

                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();

                currentPath = RecalculateTaskPath(task);
                pathIndex = 0;
                replanCount++;

                if (replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                Succeeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath = RecalculateTaskPath(task);
                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.PathBlocked
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até a tarefa foi bloqueado."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            yield return MoveTo(nextTile);

            if (!movement.LastMoveSucceeded)
            {
                movement.SnapToGrid(controller.gridPosition);

                currentPath = null;

                for (int retry = 0;
                     retry < 3 && currentPath == null;
                     retry++)
                {
                    yield return null;

                    if (movement.ShouldFall())
                    {
                        yield return movement.HandleFallingRoutine();
                    }

                    currentPath = RecalculateTaskPath(task);
                }

                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }
    public IEnumerator FollowPosition(
    Vector2Int targetPosition,
    List<Vector2Int> initialPath)
    {
        Succeeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;

        diagnostics.BeginPath(
            targetPosition,
            currentPath,
            MaximumReplans);

        while (true)
        {
            diagnostics.UpdatePath(
                currentPath,
                pathIndex,
                replanCount);

            if (currentPath == null)
            {
                diagnostics.RecordFailure(
                    TaskFailureReason.TargetUnreachable,
                    "Não existe caminho até o alvo.");

                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();

                currentPath =
                    RecalculateExactPath(targetPosition);

                pathIndex = 0;
                replanCount++;

                if (replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                Succeeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath =
                    RecalculateExactPath(targetPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.PathBlocked
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até o alvo foi bloqueado."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            yield return MoveTo(nextTile);

            if (!movement.LastMoveSucceeded)
            {
                currentPath =
                    RecalculateExactPath(targetPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }
    public IEnumerator FollowInteraction(
    Vector2Int targetPosition,
    List<Vector2Int> initialPath,
    Vector2Int? fixedInteractionPosition = null)
    {
        Succeeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;

        diagnostics.BeginPath(
            targetPosition,
            currentPath,
            MaximumReplans);

        while (true)
        {
            diagnostics.UpdatePath(
                currentPath,
                pathIndex,
                replanCount);

            if (currentPath == null)
            {
                diagnostics.RecordFailure(
                    TaskFailureReason.StorageUnreachable,
                    "Não existe caminho até a posição de interação.");

                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();

                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                if (fixedInteractionPosition.HasValue
                    && controller.gridPosition
                        != fixedInteractionPosition.Value)
                {
                    currentPath = RecalculateInteractionPath(
                        targetPosition,
                        fixedInteractionPosition);

                    pathIndex = 0;
                    replanCount++;

                    if (currentPath == null
                        || replanCount > MaximumReplans)
                    {
                        diagnostics.RecordFailure(
                            TaskFailureReason.StorageUnreachable,
                            "A posição reservada de interação ficou inacessível.");

                        yield break;
                    }

                    continue;
                }

                Succeeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.StorageUnreachable
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até a interação foi bloqueado."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            yield return MoveTo(nextTile);

            if (!movement.LastMoveSucceeded)
            {
                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null
                    || replanCount > MaximumReplans)
                {
                    diagnostics.RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido.");

                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }
    private IEnumerator MoveTo(Vector2Int nextTile)
    {
        controller.currentState =
            DuplicantController.WorkerState.Moving;

        MovementType moveType = movement.DeduceMovementType(
            controller.gridPosition,
            nextTile);

        yield return movement.MoveToTile(
            nextTile,
            moveType,
            movement.GetCurrentMoveSpeed(moveType));
    }

    private List<Vector2Int> RecalculateExactPath(
        Vector2Int targetPosition)
    {
        return PathfindingAStar.Instance?.FindPath(
            controller.gridPosition,
            targetPosition);
    }

    private List<Vector2Int> RecalculateInteractionPath(
        Vector2Int targetPosition,
        Vector2Int? fixedInteractionPosition)
    {
        if (fixedInteractionPosition.HasValue)
        {
            if (controller.gridPosition
                == fixedInteractionPosition.Value)
            {
                return emptyPath;
            }

            return PathfindingAStar.Instance?.FindPath(
                controller.gridPosition,
                fixedInteractionPosition.Value,
                controller.capabilityProfile);
        }

        return TaskNavigationUtility.GetPathToInteractionPosition(
            controller.gridPosition,
            targetPosition,
            true,
            controller.capabilityProfile);
    }

    private List<Vector2Int> RecalculateTaskPath(Task task)
    {
        return TaskNavigationUtility.GetPathToTask(
            controller.gridPosition,
            task,
            controller.capabilityProfile);
    }

    private bool CanTraverseTo(Vector2Int nextTile)
    {
        return NavGraphGenerator.Instance != null
            && NavGraphGenerator.Instance.CanTraverse(
                controller.gridPosition,
                nextTile);
    }
}