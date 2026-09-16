using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TaskCleanupService
{
    private readonly List<Task> invalidTaskBuffer =
        new List<Task>(64);

    private int lastCleanupFrame = -1;
    private bool isCleaning;

    public void Reset()
    {
        invalidTaskBuffer.Clear();
        lastCleanupFrame = -1;
        isCleaning = false;
    }

    public void Cleanup(
        List<Task> pendingTasks,
        Func<Task, bool> isTaskValid,
        Action<Task> interruptTask,
        Action<Task> removeTask,
        bool force = false)
    {
        if (isCleaning)
        {
            return;
        }

        if (!force && lastCleanupFrame == Time.frameCount)
        {
            return;
        }

        lastCleanupFrame = Time.frameCount;

        if (GridManager.Instance == null)
        {
            return;
        }

        isCleaning = true;
        invalidTaskBuffer.Clear();

        try
        {
            for (int i = 0; i < pendingTasks.Count; i++)
            {
                Task task = pendingTasks[i];

                if (!isTaskValid(task))
                {
                    invalidTaskBuffer.Add(task);
                }
            }

            for (int i = 0; i < invalidTaskBuffer.Count; i++)
            {
                Task task = invalidTaskBuffer[i];

                if (!pendingTasks.Contains(task))
                {
                    continue;
                }

                interruptTask?.Invoke(task);
                task.isAssigned = false;
                removeTask?.Invoke(task);
            }
        }
        finally
        {
            isCleaning = false;
        }
    }
}