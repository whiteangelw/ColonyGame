using UnityEngine;

public sealed class TaskSelectionDiagnostics
{
    public bool Enabled { get; set; }

    public void LogRejection(Task task, string reason)
    {
        if (!Enabled
            || task == null
            || task.targetBlueprint == null)
        {
            return;
        }

        ConstructionBlueprint blueprint =
            task.targetBlueprint;

        Tile terrain = GridManager.Instance?.GetTile(
            blueprint.gridPosition);

        string terrainDescription = terrain != null
            ? terrain.type.ToString()
            : "fora do grid";

        string message =
            $"[TaskManagerDiagnostic] Task={task.type}; "
            + $"BlueprintState={blueprint.CurrentState}; "
            + $"Target={blueprint.gridPosition}; "
            + $"Terrain={terrainDescription}; "
            + $"Resultado={reason}";

        Debug.Log(message);
    }
}