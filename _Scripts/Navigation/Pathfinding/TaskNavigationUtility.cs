using System.Collections.Generic;
using UnityEngine;

public static class TaskNavigationUtility
{
    private static readonly List<Vector2Int> candidatePositions = new List<Vector2Int>();
    private static Vector2Int candidateSortOrigin;

    /// <summary>
    /// Calcula o caminho ideal para que um colono execute uma tarefa específica.
    /// </summary>
    public static List<Vector2Int> GetPathToTask(Vector2Int startPos, Task task, DuplicantCapabilityProfile profile = null)
    {
        if (PathfindingAStar.Instance == null
            || NavGraphGenerator.Instance == null
            || task == null)
        {
            return null;
        }

        // Tarefas do tipo Haul exigem ir exatamente até o item
        if (task.type == TaskType.HaulResource && task.targetBlueprint == null)
        {
            if (task.targetItem == null
                || !task.targetItem.gameObject.activeInHierarchy
                || GridManager.Instance == null)
            {
                return null;
            }

            task.gridPosition = GridManager.Instance.WorldToGridPosition(
                task.targetItem.transform.position
            );

            return PathfindingAStar.Instance.FindPath(
                startPos,
                task.gridPosition,
                profile
            );
        }

        candidatePositions.Clear();

        // Determina se a construção criará um bloco sólido
        bool isBuildingSolid = task.type == TaskType.BuildTile && task.buildTileType != TileType.Ladder;

        DuplicantCapabilityProfile effectiveProfile = profile != null
            ? profile
            : NavGraphGenerator.Instance.DefaultProfile;

        int interactionRange = effectiveProfile != null
            ? Mathf.Max(1, effectiveProfile.buildAndDigRange)
            : 4;
        int horizontalRange = Mathf.Min(2, interactionRange);
        int maximumVerticalOffset = interactionRange - 1;

        if (GridManager.Instance != null
            && GridManager.Instance.TryGetInteractionCells(
                task.gridPosition,
                candidatePositions))
        {
            if (candidatePositions.Contains(startPos))
            {
                return new List<Vector2Int>();
            }

            return FindPathToClosestReachableCandidate(
                startPos,
                effectiveProfile);
        }

        for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
        {
            for (int dy = -1; dy <= maximumVerticalOffset; dy++)
            {
                // Se for bloco sólido, ignora a própria célula do bloco (dx=0, dy=0) para o colono não ficar dentro dele
                if (isBuildingSolid && dx == 0 && dy == 0)
                {
                    continue;
                }

                Vector2Int candidatePos = task.gridPosition - new Vector2Int(dx, dy);

                // Pergunta ao NavGraphGenerator se candidatePos é uma posição válida de parada
                if (NavGraphGenerator.Instance.IsStandablePosition(candidatePos.x, candidatePos.y)
                    && !candidatePositions.Contains(candidatePos))
                {
                    candidatePositions.Add(candidatePos);
                }
            }
        }

        // Se o colono já está em uma posição na qual consegue interagir, não precisa andar
        if (candidatePositions.Contains(startPos))
        {
            return new List<Vector2Int>();
        }

        return FindPathToClosestReachableCandidate(
            startPos,
            effectiveProfile
        );
    }

    public static List<Vector2Int> GetPathToInteractionPosition(
        Vector2Int startPos,
        Vector2Int targetPos,
        bool excludeTargetCell = true,
        DuplicantCapabilityProfile profile = null)
    {
        if (PathfindingAStar.Instance == null
            || NavGraphGenerator.Instance == null)
        {
            return null;
        }

        candidatePositions.Clear();

        DuplicantCapabilityProfile effectiveProfile = profile != null
            ? profile
            : NavGraphGenerator.Instance.DefaultProfile;

        int interactionRange = effectiveProfile != null
            ? Mathf.Max(1, effectiveProfile.buildAndDigRange)
            : 4;
        int horizontalRange = Mathf.Min(2, interactionRange);
        int maximumVerticalOffset = interactionRange - 1;

        if (GridManager.Instance != null
            && GridManager.Instance.TryGetInteractionCells(
                targetPos,
                candidatePositions))
        {
            if (candidatePositions.Contains(startPos))
            {
                return new List<Vector2Int>();
            }

            return FindPathToClosestReachableCandidate(
                startPos,
                effectiveProfile);
        }

        for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
        {
            for (int dy = -1; dy <= maximumVerticalOffset; dy++)
            {
                if (excludeTargetCell && dx == 0 && dy == 0)
                {
                    continue;
                }

                Vector2Int candidatePos =
                    targetPos - new Vector2Int(dx, dy);

                if (NavGraphGenerator.Instance.IsStandablePosition(
                        candidatePos.x,
                        candidatePos.y)
                    && !candidatePositions.Contains(candidatePos))
                {
                    candidatePositions.Add(candidatePos);
                }
            }
        }

        if (candidatePositions.Contains(startPos))
        {
            return new List<Vector2Int>();
        }

        return FindPathToClosestReachableCandidate(
            startPos,
            effectiveProfile
        );
    }

    private static List<Vector2Int> FindPathToClosestReachableCandidate(
        Vector2Int startPos,
        DuplicantCapabilityProfile profile)
    {
        candidateSortOrigin = startPos;
        candidatePositions.Sort(CompareCandidateDistance);

        ReachabilityManager reachability = ReachabilityManager.Instance;

        foreach (Vector2Int candidate in candidatePositions)
        {
            if (reachability != null
                && reachability.IsReady
                && !reachability.CanReachExact(
                    startPos,
                    candidate,
                    profile))
            {
                continue;
            }

            List<Vector2Int> path = PathfindingAStar.Instance.FindPath(
                startPos,
                candidate,
                profile
            );

            if (path != null)
            {
                return path;
            }
        }

        return null;
    }

    private static int CompareCandidateDistance(
        Vector2Int a,
        Vector2Int b)
    {
        int distanceA = SquaredDistance(candidateSortOrigin, a);
        int distanceB = SquaredDistance(candidateSortOrigin, b);
        return distanceA.CompareTo(distanceB);
    }

    private static int SquaredDistance(Vector2Int a, Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
