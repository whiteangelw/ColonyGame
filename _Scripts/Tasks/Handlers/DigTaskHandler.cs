using System;

public class DigTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.Dig;

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        return task != null
            && WorldInteractionService.Instance != null
            && WorldInteractionService.Instance.CanDigTile(
                task.gridPosition.x,
                task.gridPosition.y,
                out _);
    }

    public void StartTask(DuplicantController duplicant, Task task, Action onComplete)
    {
        // Redirecionado para o serviço de interação
        WorldInteractionService.Instance?.DigTile(task.gridPosition.x, task.gridPosition.y);
        task.targetBlueprint?.NotifyTaskEnded(task);
        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }

    public void StopTask(DuplicantController duplicant, Task task) { }
}
