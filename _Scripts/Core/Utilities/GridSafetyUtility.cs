using System.Collections.Generic;
using UnityEngine;

public static class GridSafetyUtility
{
    /// <summary>
    /// Ejeta colonos e itens da célula quando ela se torna sólida.
    /// </summary>
    public static void EjectEntitiesFromTile(int x, int y)
    {
        if (GridManager.Instance == null) return;

        Vector2Int targetTile = new Vector2Int(x, y);
        Vector2Int safeTile = FindNearestStandableTileBFS(targetTile);

        float cs = GridManager.Instance.cellSize;
        Vector3 safeWorldPos = new Vector3(safeTile.x * cs + cs / 2f, safeTile.y * cs, 0f);

        DuplicantController[] dupes = Object.FindObjectsByType<DuplicantController>(FindObjectsSortMode.None);
        foreach (var dupe in dupes)
        {
            if (dupe == null) continue;

            Vector2Int dupePos = GridManager.Instance.WorldToGridPosition(dupe.transform.position);

            if (dupePos == targetTile || dupe.gridPosition == targetTile)
            {
                // Para a rotina atual imediatamente para não sobrescrever a posição no frame seguinte
                dupe.StopAllCoroutines();

                dupe.transform.position = safeWorldPos;
                dupe.gridPosition = safeTile;
                dupe.currentState = DuplicantController.WorkerState.Idle;
                dupe.currentTask = null;

                if (dupe.Brain != null)
                {
                    dupe.Brain.SetSearchCooldown(0.5f);
                }

                DuplicantMovement movement = dupe.Movement;
                if (movement != null && movement.ShouldFall())
                {
                    dupe.StartCoroutine(movement.HandleFallingRoutine());
                }
            }
        }
    }

    /// <summary>
    /// Busca BFS que garante encontrar uma célula onde o colono possa efetivamente FICAR EM PÉ
    /// respeitando a verificação de 2 blocos de altura e chão/escada.
    /// </summary>
    public static Vector2Int FindNearestStandableTileBFS(Vector2Int origin)
    {
        if (GridManager.Instance == null) return origin;

        NavGraphGenerator nav = NavGraphGenerator.Instance;

        // Se a origem for um local válido para ficar em pé, retorna ela
        if (nav != null && nav.IsStandablePosition(origin.x, origin.y))
        {
            return origin;
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(origin);
        visited.Add(origin);

        // Prioridade: Esquerda, Direita, Cima, Diagonais e Baixo
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(-1, 0), // Esquerda
            new Vector2Int(1, 0),  // Direita
            new Vector2Int(0, 1),  // Cima
            new Vector2Int(-1, 1), // Diagonal Sup Esquerda
            new Vector2Int(1, 1),  // Diagonal Sup Direita
            new Vector2Int(0, -1)  // Baixo
        };

        int maxSearch = 80;
        int count = 0;

        while (queue.Count > 0 && count < maxSearch)
        {
            Vector2Int current = queue.Dequeue();
            count++;

            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;

                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);

                    // Valida através do NavGraphGenerator se o nó é Standable (2 blocos vagos + chão sólido/escada)
                    if (nav != null && nav.IsStandablePosition(neighbor.x, neighbor.y))
                    {
                        return neighbor;
                    }
                    else if (GridManager.Instance.IsPassable(neighbor.x, neighbor.y))
                    {
                        // Fallback se o NavGraph ainda não tiver sido regenerado
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return origin + Vector2Int.up;
    }
}