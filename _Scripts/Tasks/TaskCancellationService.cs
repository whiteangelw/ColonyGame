using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TaskCancellationService
{
    public bool CancelTasksAt(
        List<Task> pendingTasks,
        Vector2Int position,
        Action<Task> removeTask)
    {
        bool cancelled = false;

        List<Task> matches = pendingTasks.FindAll(
            task => task != null
                && task.gridPosition == position);

        foreach (Task task in matches)
        {
            PerformanceMetricsService.RecordGlobalObjectSearch();

            DuplicantController[] duplicants =
                UnityEngine.Object.FindObjectsByType<DuplicantController>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (DuplicantController duplicant in duplicants)
            {
                if (duplicant != null
                    && duplicant.currentTask == task)
                {
                    duplicant.CancelCurrentTaskExecution(
                        TaskInterruptionOrigin.ManualCancellation);
                }
            }

            task.isAssigned = false;
            removeTask?.Invoke(task);
            cancelled = true;
        }

        return cancelled;
    }

    public int CancelTasksForBlueprint(
        List<Task> pendingTasks,
        ConstructionBlueprint blueprint,
        Action<Task> removeTask)
    {
        if (blueprint == null)
        {
            return 0;
        }

        List<Task> matches = pendingTasks.FindAll(
            task => task != null
                && task.targetBlueprint == blueprint);

        foreach (Task task in matches)
        {
            InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.BlueprintCancelled,
                true);

            task.isAssigned = false;
            removeTask?.Invoke(task);
        }

        return matches.Count;
    }

    public void InterruptAssignedTask(
        Task task,
        TaskInterruptionOrigin origin,
        bool preserveCarriedInventory = false,
        bool skipWorkerAlreadyExecuting = false)
    {
        if (task == null || !task.isAssigned)
        {
            return;
        }

        PerformanceMetricsService.RecordGlobalObjectSearch();

        DuplicantController[] duplicants =
            UnityEngine.Object.FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (DuplicantController duplicant in duplicants)
        {
            if (duplicant == null
                || duplicant.currentTask != task)
            {
                continue;
            }

            if (skipWorkerAlreadyExecuting
                && duplicant.currentState
                    == DuplicantController.WorkerState.Working)
            {
                continue;
            }

            duplicant.CancelCurrentTaskExecution(
                origin,
                preserveCarriedInventory);
        }
    }
}