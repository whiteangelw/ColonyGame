using UnityEngine;

public class DismantleTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.Dismantle;

    public bool CanExecute(DuplicantController dupe, Task task)
    {
        if (task == null
            || task.type != TaskType.Dismantle
            || GridManager.Instance == null)
        {
            return false;
        }

        return WorldInteractionService.Instance != null
            && WorldInteractionService.Instance.CanDismantleAt(
                task.gridPosition,
                task.targetLayer);
    }

    public void StartTask(DuplicantController dupe, Task task, System.Action onComplete)
    {
        if (dupe == null || task == null) return;

        // Executa o desmonte físico e a geração de reembolso
        WorldInteractionService.Instance?.ExecuteDismantleAt(
            task.gridPosition.x,
            task.gridPosition.y,
            task.targetLayer);
        onComplete?.Invoke();
    }
    public void UpdateTask(DuplicantController duplicant, Task task) { }

    public void StopTask(DuplicantController duplicant, Task task) { }
}
