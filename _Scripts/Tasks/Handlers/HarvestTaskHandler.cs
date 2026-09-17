using System;
using UnityEngine;

public sealed class HarvestTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.Harvest;

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        // Durante a seleção, TaskSelectionService ainda não possui o
        // DuplicantController e consulta o handler passando null.
        return task != null
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
        bool harvested = task.targetFlora.TryHarvest(
            out ResourceType resourceType,
            out int amount);

        if (harvested && amount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(
                resourceType,
                dropPosition,
                amount);
            GameEvents.TriggerFloatingTextRequested(
                $"+{amount} {resourceType}",
                dropPosition,
                Color.green);
        }

        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }
    public void StopTask(DuplicantController duplicant, Task task) { }
}
