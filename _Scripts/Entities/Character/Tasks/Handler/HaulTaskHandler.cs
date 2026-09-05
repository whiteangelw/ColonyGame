using System;
using UnityEngine;

public class HaulTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.HaulResource;

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        if (task == null) return false;

        // Se for um blueprint precisando de entregas
        if (task.targetBlueprint != null)
        {
            return task.targetBlueprint.CurrentState == BlueprintState.WaitingMaterials &&
                   task.targetBlueprint.GetRemainingNeededAmount() > 0 &&
                   StockpileManager.Instance != null &&
                   StockpileManager.Instance.GetAvailableAmount(
                       task.targetBlueprint.requiredResource
                   ) > 0;
        }

        // Se for uma coleta simples de item no chão
        if (task.targetItem == null
            || !task.targetItem.gameObject.activeInHierarchy
            || task.targetItem.amount <= 0
            || GridManager.Instance == null
            || StructureManager.Instance == null)
        {
            return false;
        }

        Vector2Int itemPosition =
            GridManager.Instance.WorldToGridPosition(
                task.targetItem.transform.position
            );

        return StructureManager.Instance.HasReachableStorageFor(
            task.targetItem.type,
            1,
            itemPosition
        );
    }

    public void StartTask(DuplicantController duplicant, Task task, Action onComplete)
    {
        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }

    public void StopTask(DuplicantController duplicant, Task task) { }
}
