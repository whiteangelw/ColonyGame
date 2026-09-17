using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TaskSelectionService
{
    private readonly List<TaskSelectionCandidate> candidateBuffer =
        new List<TaskSelectionCandidate>(512);

    public Task TrySelect(
        List<Task> pendingTasks,
        Vector2Int workerPosition,
        DuplicantCapabilityProfile profile,
        DuplicantWorkProfile workProfile,
        Dictionary<TaskType, ITaskHandler> handlers,
        Action<Task, string> logRejection,
        out List<Vector2Int> calculatedPath,
        Predicate<Task> isTaskTemporarilyDeferred = null)
    {
        calculatedPath = null;
        candidateBuffer.Clear();

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];

            if (task == null || task.isAssigned)
            {
                continue;
            }

            if (isTaskTemporarilyDeferred != null
                && isTaskTemporarilyDeferred(task))
            {
                continue;
            }

            RefreshDynamicTaskPosition(task);

            candidateBuffer.Add(new TaskSelectionCandidate(
                task,
                CalculateScore(task, workerPosition, workProfile),
                SquaredGridDistance(workerPosition, task.gridPosition)));
        }

        candidateBuffer.Sort(TaskSelectionCandidate.Compare);

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            Task task = candidateBuffer[i].Task;

            if (!HasAvailableBlueprintSupply(task))
            {
                logRejection?.Invoke(
                    task,
                    "nenhum insumo disponível para abastecer o blueprint");

                continue;
            }

            bool isGroundHaul = task.type == TaskType.HaulResource
                && task.targetBlueprint == null;

            if (isGroundHaul
                && ReachabilityManager.Instance != null
                && ReachabilityManager.Instance.IsReady
                && !ReachabilityManager.Instance.CanReachExact(
                    workerPosition,
                    task.gridPosition,
                    profile))
            {
                logRejection?.Invoke(
                    task,
                    "o item exato não está alcançável");

                continue;
            }

            if (!handlers.TryGetValue(task.type, out ITaskHandler handler)
                || handler == null
                || !handler.CanExecute(null, task))
            {
                logRejection?.Invoke(
                    task,
                    "o handler recusou o estado atual");

                continue;
            }

            List<Vector2Int> path = TaskNavigationUtility.GetPathToTask(
                workerPosition,
                task,
                profile);

            if (path == null)
            {
                logRejection?.Invoke(
                    task,
                    "nenhum interaction candidate alcançável gerou path");

                continue;
            }

            task.isAssigned = true;

            logRejection?.Invoke(task, "aceita; path encontrado");

            calculatedPath = path;
            return task;
        }

        return null;
    }

    public static int CalculateScore(
        Task task,
        Vector2Int workerPosition,
        DuplicantWorkProfile workProfile = null)
    {
        if (task == null)
        {
            return int.MinValue;
        }

        int globalPriorityScore = task.priority * 1000;

        int affinityScore = workProfile != null
            ? workProfile.GetAffinity(task.type) * 100
            : 0;

        int gridDistance = Mathf.Abs(
            workerPosition.x - task.gridPosition.x)
            + Mathf.Abs(workerPosition.y - task.gridPosition.y);

        int distancePenalty = Mathf.Min(gridDistance, 99);

        return globalPriorityScore
            + affinityScore
            - distancePenalty;
    }

    private static bool HasAvailableBlueprintSupply(Task task)
    {
        if (task == null
            || task.type != TaskType.HaulResource
            || task.targetBlueprint == null)
        {
            return true;
        }

        ConstructionBlueprint blueprint = task.targetBlueprint;
        StockpileManager stockpile = StockpileManager.Instance;

        return blueprint.CurrentState == BlueprintState.WaitingMaterials
            && blueprint.GetRemainingNeededAmount() > 0
            && stockpile != null
            && stockpile.GetAvailableAmount(
                blueprint.requiredResource) > 0;
    }

    private static void RefreshDynamicTaskPosition(Task task)
    {
        if (task.type != TaskType.HaulResource
            || task.targetBlueprint != null
            || task.targetItem == null
            || GridManager.Instance == null)
        {
            return;
        }

        task.gridPosition = GridManager.Instance.WorldToGridPosition(
            task.targetItem.transform.position);
    }

    private static int SquaredGridDistance(
        Vector2Int a,
        Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;

        return deltaX * deltaX + deltaY * deltaY;
    }

    private readonly struct TaskSelectionCandidate
    {
        public readonly Task Task;
        private readonly int score;
        private readonly int distanceSquared;

        public TaskSelectionCandidate(
            Task task,
            int score,
            int distanceSquared)
        {
            Task = task;
            this.score = score;
            this.distanceSquared = distanceSquared;
        }

        public static int Compare(
            TaskSelectionCandidate a,
            TaskSelectionCandidate b)
        {
            int scoreComparison = b.score.CompareTo(a.score);

            return scoreComparison != 0
                ? scoreComparison
                : a.distanceSquared.CompareTo(b.distanceSquared);
        }
    }

    public void Clear()
    {
        candidateBuffer.Clear();
    }
}
