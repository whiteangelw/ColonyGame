using System.Collections.Generic;

public sealed class TaskHandlerRegistry
{
    private readonly Dictionary<TaskType, ITaskHandler> handlers =
        new Dictionary<TaskType, ITaskHandler>();

    public Dictionary<TaskType, ITaskHandler> Handlers =>
        handlers;

    public bool Register(ITaskHandler handler)
    {
        if (handler == null
            || handlers.ContainsKey(handler.HandledType))
        {
            return false;
        }

        handlers.Add(handler.HandledType, handler);
        return true;
    }

    public ITaskHandler Get(TaskType taskType)
    {
        handlers.TryGetValue(
            taskType,
            out ITaskHandler handler);

        return handler;
    }

    public void Execute(
        Task task,
        DuplicantController duplicant)
    {
        if (task == null)
        {
            return;
        }

        ITaskHandler handler = Get(task.type);
        handler?.StartTask(duplicant, task, null);
    }
}