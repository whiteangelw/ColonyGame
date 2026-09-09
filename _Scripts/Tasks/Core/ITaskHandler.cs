using System;

public interface ITaskHandler
{
    TaskType HandledType { get; }
    bool CanExecute(DuplicantController duplicant, Task task);
    void StartTask(DuplicantController duplicant, Task task, Action onComplete);
    void UpdateTask(DuplicantController duplicant, Task task);
    void StopTask(DuplicantController duplicant, Task task);
}