using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TaskWorkExecutionResult
{
    None,
    Completed,
    PathBlocked,
    TargetInvalid,
    TargetDestroyedBeforeWork,
    TargetDestroyedDuringWork,
    MovementFailed
}

/// <summary>
/// Executa o trabalho após uma tarefa já ter sido atribuída.
/// Não controla inventário, reservas ou seleção de tarefas.
/// </summary>
public sealed class DuplicantWorkExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantMovement movement;
    private readonly DuplicantPathFollower pathFollower;

    public TaskWorkExecutionResult Result { get; private set; }

    public DuplicantWorkExecutor(
        DuplicantController controller,
        DuplicantMovement movement,
        DuplicantPathFollower pathFollower)
    {
        this.controller = controller;
        this.movement = movement;
        this.pathFollower = pathFollower;
    }

    public IEnumerator ExecuteAssembly(
        Task task,
        List<Vector2Int> path)
    {
        Result = TaskWorkExecutionResult.None;

        ConstructionBlueprint blueprint = task.targetBlueprint;

        if (blueprint == null
            || blueprint.CurrentState != BlueprintState.ReadyToBuild)
        {
            Result = TaskWorkExecutionResult.TargetInvalid;
            yield break;
        }

        Vector2Int targetPosition = blueprint.gridPosition;

        yield return pathFollower.FollowTask(task, path);

        if (!pathFollower.Succeeded)
        {
            Result = TaskWorkExecutionResult.PathBlocked;
            yield break;
        }

        if (blueprint == null)
        {
            Result =
                TaskWorkExecutionResult.TargetDestroyedBeforeWork;

            yield break;
        }

        if (controller.gridPosition == targetPosition
            && blueprint.targetTileType != TileType.Ladder)
        {
            Vector2Int safePosition =
                GridSafetyUtility.FindNearestStandableTileBFS(
                    controller.gridPosition);

            if (safePosition != controller.gridPosition)
            {
                MovementType moveType =
                    movement.DeduceMovementType(
                        controller.gridPosition,
                        safePosition);

                yield return movement.MoveToTile(
                    safePosition,
                    moveType,
                    movement.GetCurrentMoveSpeed(moveType));

                if (!movement.LastMoveSucceeded)
                {
                    Result = TaskWorkExecutionResult.MovementFailed;
                    yield break;
                }
            }
        }

        controller.currentState =
            DuplicantController.WorkerState.Working;

        bool completed = false;

        while (!completed && blueprint != null)
        {
            float workDelta = Time.deltaTime
                * controller.WorkEfficiencyMultiplier;

            completed = blueprint.ApplyWork(workDelta);

            yield return null;
        }

        Result = completed
            ? TaskWorkExecutionResult.Completed
            : TaskWorkExecutionResult.TargetDestroyedDuringWork;
    }

    public IEnumerator ExecuteGeneric(
        Task task,
        List<Vector2Int> path)
    {
        Result = TaskWorkExecutionResult.None;

        yield return pathFollower.FollowTask(task, path);

        if (!pathFollower.Succeeded)
        {
            Result = TaskWorkExecutionResult.PathBlocked;
            yield break;
        }

        if (TaskManager.Instance == null
            || !TaskManager.Instance.IsTaskValid(task))
        {
            Result = TaskWorkExecutionResult.TargetDestroyedBeforeWork;
            yield break;
        }

        controller.currentState =
            DuplicantController.WorkerState.Working;

        while (TaskManager.Instance != null
            && TaskManager.Instance.IsTaskValid(task)
            && !task.ApplyWork(
                Time.deltaTime
                * Mathf.Max(0.05f, controller.WorkEfficiencyMultiplier)))
        {
            yield return null;
        }

        if (TaskManager.Instance == null
            || !TaskManager.Instance.IsTaskValid(task))
        {
            Result = TaskWorkExecutionResult.TargetDestroyedDuringWork;
            yield break;
        }

        TaskManager.Instance.ExecuteTask(task, controller);
        TaskManager.Instance.RemoveTask(task);

        Result = TaskWorkExecutionResult.Completed;
    }
}
