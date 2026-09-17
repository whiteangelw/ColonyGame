using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mantém os dados de diagnóstico da tarefa atual.
/// Não executa tarefas, caminhos ou reservas.
/// </summary>
public sealed class DuplicantTaskDiagnostics
{
    public TaskFailureReason LastFailureReason { get; private set; }
    public TaskInterruptionOrigin LastInterruptionOrigin { get; private set; }
    public string LastFailureDetails { get; private set; } = "Nenhuma";
    public float LastFailureTime { get; private set; } = -1f;

    public Vector2Int? Destination { get; private set; }
    public int PathLength { get; private set; }
    public int PathIndex { get; private set; }
    public int ReplanCount { get; private set; }
    public int MaxReplans { get; private set; }

    public bool HasFailureForActiveTask { get; private set; }

    public void BeginTask()
    {
        HasFailureForActiveTask = false;
        ResetPath();
    }

    public void RecordFailure(
        TaskFailureReason reason,
        string details)
    {
        LastFailureReason = reason;
        LastFailureDetails = string.IsNullOrWhiteSpace(details)
            ? reason.ToString()
            : details;

        LastFailureTime = Time.time;
        HasFailureForActiveTask = true;
    }

    public void RecordInformation(string details)
    {
        LastFailureReason = TaskFailureReason.None;
        LastFailureDetails = string.IsNullOrWhiteSpace(details)
            ? "Nenhuma"
            : details;

        LastFailureTime = Time.time;
        HasFailureForActiveTask = false;
    }

    public void SetInterruptionOrigin(TaskInterruptionOrigin origin)
    {
        LastInterruptionOrigin = origin;
    }

    public void ClearFailure()
    {
        LastFailureReason = TaskFailureReason.None;
        LastInterruptionOrigin = TaskInterruptionOrigin.None;
        LastFailureDetails = "Nenhuma";
        LastFailureTime = -1f;
        HasFailureForActiveTask = false;
    }

    public void BeginPath(
        Vector2Int destination,
        List<Vector2Int> path,
        int maxReplans)
    {
        Destination = destination;
        PathLength = path != null ? path.Count : 0;
        PathIndex = 0;
        ReplanCount = 0;
        MaxReplans = maxReplans;
    }

    public void UpdatePath(
        List<Vector2Int> path,
        int pathIndex,
        int replanCount)
    {
        PathLength = path != null ? path.Count : 0;
        PathIndex = pathIndex;
        ReplanCount = replanCount;
    }

    public void ResetPath()
    {
        Destination = null;
        PathLength = 0;
        PathIndex = 0;
        ReplanCount = 0;
        MaxReplans = 0;
    }
}