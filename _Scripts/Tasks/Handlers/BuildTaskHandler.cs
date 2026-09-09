using System;

public class BuildTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.BuildTile;

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        // Se a tarefa veio de um blueprint, só aceita se o blueprint já estiver 100% abastecido
        if (task?.targetBlueprint != null)
        {
            return task.targetBlueprint.CurrentState == BlueprintState.ReadyToBuild;
        }
        return false;
    }

    public void StartTask(DuplicantController duplicant, Task task, Action onComplete)
    {
        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }

    public void StopTask(DuplicantController duplicant, Task task) { }
}