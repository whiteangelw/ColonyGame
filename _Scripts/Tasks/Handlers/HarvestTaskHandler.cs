using System;
using UnityEngine;

public sealed class HarvestTaskHandler : ITaskHandler
{
    public TaskType HandledType { get; }

    public HarvestTaskHandler(TaskType handledType = TaskType.Harvest)
    {
        if (handledType != TaskType.Harvest
            && handledType != TaskType.Chop)
        {
            throw new ArgumentOutOfRangeException(nameof(handledType));
        }

        HandledType = handledType;
    }

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        // Durante a seleção, TaskSelectionService ainda não possui o
        // DuplicantController e consulta o handler passando null.
        return task != null
            && task.type == HandledType
            && task.targetFlora != null
            && task.targetFlora.CanHarvest;
    }

    public void StartTask(
        DuplicantController duplicant,
        Task task,
        Action onComplete)
    {
        if (duplicant == null || !CanExecute(duplicant, task)) return;

        Vector3 dropPosition = task.targetFlora.transform.position;
        bool harvested = task.targetFlora.TryHarvest(out var results);

        if (harvested)
        {
            foreach (ResourceAmount result in results)
            {
                if (result.Amount <= 0) continue;
                ItemSpawner.Instance?.SpawnResource(
                    result.ResourceType,
                    dropPosition,
                    result.Amount);
                GameEvents.TriggerFloatingTextRequested(
                    $"+{result.Amount} {result.ResourceType}",
                    dropPosition,
                    Color.green);
            }
        }

        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }
    public void StopTask(DuplicantController duplicant, Task task) { }
}
