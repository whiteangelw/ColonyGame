using System;

public sealed class OperateMachineTaskHandler : ITaskHandler
{
    public TaskType HandledType => TaskType.OperateMachine;

    public bool CanExecute(DuplicantController duplicant, Task task)
    {
        return task != null
            && task.targetProductionMachine != null
            && task.targetProductionMachine.CanOperate;
    }

    public void StartTask(DuplicantController duplicant, Task task, Action onComplete)
    {
        onComplete?.Invoke();
    }

    public void UpdateTask(DuplicantController duplicant, Task task) { }
    public void StopTask(DuplicantController duplicant, Task task) { }
}
