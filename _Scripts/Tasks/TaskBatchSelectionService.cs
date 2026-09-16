using System.Collections.Generic;
using UnityEngine;

public sealed class TaskBatchSelectionService
{
    private readonly List<Task> candidateBuffer =
        new List<Task>(64);

    private Vector2Int sortOrigin;

    public void Clear()
    {
        candidateBuffer.Clear();
    }

    public bool TrySelectBlueprintDeliveryTask(
        List<Task> pendingTasks,
        Vector2Int workerPosition,
        Vector2Int batchAnchor,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        int maximumDeliveryDistance,
        int maximumPathChecks,
        out Task selectedTask)
    {
        selectedTask = null;
        candidateBuffer.Clear();

        int maximumDistanceSquared =
            maximumDeliveryDistance * maximumDeliveryDistance;

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];

            if (task == null
                || task.isAssigned
                || task.type != TaskType.HaulResource
                || task.targetBlueprint == null
                || task.targetBlueprint.CurrentState
                    != BlueprintState.WaitingMaterials
                || task.targetBlueprint.requiredResource != resourceType
                || task.targetBlueprint.GetRemainingNeededAmount() <= 0
                || SquaredGridDistance(batchAnchor, task.gridPosition)
                    > maximumDistanceSquared)
            {
                continue;
            }

            candidateBuffer.Add(task);
        }

        sortOrigin = batchAnchor;
        candidateBuffer.Sort(CompareBlueprintCandidates);

        int checks = Mathf.Min(
            maximumPathChecks,
            candidateBuffer.Count);

        for (int i = 0; i < checks; i++)
        {
            Task candidate = candidateBuffer[i];

            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToTask(
                    workerPosition,
                    candidate,
                    profile);

            if (path == null)
            {
                continue;
            }

            candidate.isAssigned = true;
            selectedTask = candidate;
            return true;
        }

        return false;
    }

    public bool TrySelectGroundHaulTask(
        List<Task> pendingTasks,
        Vector2Int workerPosition,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        int maximumPickupDistance,
        int maximumPathChecks,
        out Task selectedTask,
        out List<Vector2Int> calculatedPath)
    {
        selectedTask = null;
        calculatedPath = null;
        candidateBuffer.Clear();

        if (GridManager.Instance == null)
        {
            return false;
        }

        int maximumDistanceSquared =
            maximumPickupDistance * maximumPickupDistance;

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];

            if (task == null
                || task.isAssigned
                || task.type != TaskType.HaulResource
                || task.targetBlueprint != null
                || task.targetItem == null
                || task.targetItem.type != resourceType
                || !task.targetItem.IsReadyForHaul)
            {
                continue;
            }

            task.gridPosition =
                GridManager.Instance.WorldToGridPosition(
                    task.targetItem.transform.position);

            if (SquaredGridDistance(
                    workerPosition,
                    task.gridPosition) > maximumDistanceSquared)
            {
                continue;
            }

            candidateBuffer.Add(task);
        }

        sortOrigin = workerPosition;
        candidateBuffer.Sort(CompareGroundHaulCandidates);

        int checks = Mathf.Min(
            maximumPathChecks,
            candidateBuffer.Count);

        for (int i = 0; i < checks; i++)
        {
            Task candidate = candidateBuffer[i];

            if (ReachabilityManager.Instance != null
                && ReachabilityManager.Instance.IsReady
                && !ReachabilityManager.Instance.CanReachExact(
                    workerPosition,
                    candidate.gridPosition,
                    profile))
            {
                continue;
            }

            List<Vector2Int> path =
                PathfindingAStar.Instance?.FindPath(
                    workerPosition,
                    candidate.gridPosition,
                    profile);

            if (path == null)
            {
                continue;
            }

            candidate.isAssigned = true;
            selectedTask = candidate;
            calculatedPath = path;

            return true;
        }

        return false;
    }

    private int CompareBlueprintCandidates(Task a, Task b)
    {
        int priorityComparison = b.priority.CompareTo(a.priority);

        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        return SquaredGridDistance(sortOrigin, a.gridPosition)
            .CompareTo(
                SquaredGridDistance(sortOrigin, b.gridPosition));
    }

    private int CompareGroundHaulCandidates(Task a, Task b)
    {
        return SquaredGridDistance(sortOrigin, a.gridPosition)
            .CompareTo(
                SquaredGridDistance(sortOrigin, b.gridPosition));
    }

    private static int SquaredGridDistance(
        Vector2Int a,
        Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;

        return deltaX * deltaX + deltaY * deltaY;
    }
}