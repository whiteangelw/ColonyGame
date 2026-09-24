using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ProductionOperationResult
{
    None,
    BatchCompleted,
    InvalidTarget,
    DestinationUnreachable
}

public sealed class ProductionOperationExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantPathFollower pathFollower;

    public ProductionOperationResult Result { get; private set; }

    public ProductionOperationExecutor(
        DuplicantController controller,
        DuplicantPathFollower pathFollower)
    {
        this.controller = controller;
        this.pathFollower = pathFollower;
    }

    public IEnumerator Execute(Task task)
    {
        Result = ProductionOperationResult.None;
        ProductionMachineBehaviour machine = task?.targetProductionMachine;
        if (machine == null || !machine.CanOperate)
        {
            Result = ProductionOperationResult.InvalidTarget;
            yield break;
        }

        List<Vector2Int> path = TaskNavigationUtility.GetPathToInteractionPosition(
            controller.gridPosition,
            machine.GridPosition);
        if (path == null)
        {
            Result = ProductionOperationResult.DestinationUnreachable;
            yield break;
        }

        yield return pathFollower.FollowInteraction(machine.GridPosition, path);
        if (!pathFollower.Succeeded)
        {
            Result = ProductionOperationResult.DestinationUnreachable;
            yield break;
        }

        controller.currentState = DuplicantController.WorkerState.Working;
        while (machine != null && machine.CanOperate)
        {
            bool batchCompleted = machine.ApplyOperationWork(
                Time.deltaTime
                * Mathf.Max(0.05f, controller.WorkEfficiencyMultiplier));
            if (batchCompleted)
            {
                Result = ProductionOperationResult.BatchCompleted;
                yield break;
            }
            yield return null;
        }

        Result = ProductionOperationResult.InvalidTarget;
    }
}
