using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TaskRegistry
{
    private readonly List<Task> tasks = new List<Task>();

    public event Action<Task> TaskAdded;
    public event Action<Task> TaskRemoved;

    public List<Task> Tasks => tasks;

    public int Count => tasks.Count;

    public int AssignedCount
    {
        get
        {
            int count = 0;

            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i] != null && tasks[i].isAssigned)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool Add(Task task)
    {
        if (task == null || tasks.Exists(
                existing => IsSameTask(existing, task)))
        {
            return false;
        }

        tasks.Add(task);
        TaskAdded?.Invoke(task);
        return true;
    }

    public bool Remove(Task task)
    {
        if (task == null || !tasks.Remove(task))
        {
            return false;
        }

        TaskRemoved?.Invoke(task);
        return true;
    }

    public void Release(Task task)
    {
        if (task != null && tasks.Contains(task))
        {
            task.isAssigned = false;
        }
    }

    public Task GetAt(Vector2Int gridPosition)
    {
        for (int i = 0; i < tasks.Count; i++)
        {
            Task task = tasks[i];

            if (task != null && task.gridPosition == gridPosition)
            {
                return task;
            }
        }

        return null;
    }

    public Task GetAt(
        Vector2Int gridPosition,
        TaskType type,
        GridLayer targetLayer)
    {
        for (int i = 0; i < tasks.Count; i++)
        {
            Task task = tasks[i];

            if (task != null
                && task.gridPosition == gridPosition
                && task.type == type
                && task.targetLayer == targetLayer)
            {
                return task;
            }
        }

        return null;
    }

    public bool Contains(Task task)
    {
        return task != null && tasks.Contains(task);
    }

    public List<Task> GetSnapshot()
    {
        return new List<Task>(tasks);
    }

    public void ClearForLoad()
    {
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            Task task = tasks[i];
            tasks.RemoveAt(i);

            if (task != null)
            {
                task.isAssigned = false;
                TaskRemoved?.Invoke(task);
            }
        }
    }

    private static bool IsSameTask(
        Task existing,
        Task candidate)
    {
        if (existing == null
            || candidate == null
            || existing.type != candidate.type)
        {
            return false;
        }

        if (candidate.type == TaskType.HaulResource)
        {
            if (candidate.targetBlueprint != null)
            {
                return existing.targetBlueprint
                    == candidate.targetBlueprint;
            }

            return candidate.targetItem != null
                && existing.targetItem == candidate.targetItem;
        }

        if (candidate.type == TaskType.Dismantle)
        {
            return existing.gridPosition == candidate.gridPosition
                && existing.targetLayer == candidate.targetLayer;
        }

        return existing.gridPosition == candidate.gridPosition;
    }
}