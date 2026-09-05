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

        Tile tile = GridManager.Instance.GetTile(task.gridPosition);

        return tile != null
            && (tile.type == TileType.Chest
                || tile.type == TileType.Ladder
                || tile.type == TileType.PrintingPod);
    }

    public void StartTask(DuplicantController dupe, Task task, System.Action onComplete)
    {
        if (dupe == null || task == null) return;

        // Executa o desmonte físico e a geração de reembolso
        WorldInteractionService.Instance?.ExecuteDismantleAt(task.gridPosition.x, task.gridPosition.y);
        onComplete?.Invoke();
    }
    public void UpdateTask(DuplicantController duplicant, Task task) { }

    public void StopTask(DuplicantController duplicant, Task task) { }
}
