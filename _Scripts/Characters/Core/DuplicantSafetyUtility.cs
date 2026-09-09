using System.Collections.Generic;
using UnityEngine;

public static class DuplicantSafetyUtility
{
    /// <summary>
    /// Força o desempilhamento de colonos que estejam no mesmo tile ou soterrados em blocos sólidos.
    /// </summary>
    public static void ResolveOverlappingDuplicants(Vector2Int targetTile)
    {
        if (GridManager.Instance == null) return;

        DuplicantController[] dupes = Object.FindObjectsByType<DuplicantController>(FindObjectsSortMode.None);
        List<DuplicantController> trappedDupes = new List<DuplicantController>();

        // Filtra quais colonos estão ocupando o tile preso/sólido
        foreach (var dupe in dupes)
        {
            if (dupe == null) continue;

            Vector2Int currentGrid = GridManager.Instance.WorldToGridPosition(dupe.transform.position);
            if (currentGrid == targetTile || dupe.gridPosition == targetTile)
            {
                trappedDupes.Add(dupe);
            }
        }

        if (trappedDupes.Count == 0) return;

        // Distribui cada colono para uma célula vizinha diferente livre
        List<Vector2Int> availableNeighbors = GetAvailableNeighborTiles(targetTile, trappedDupes.Count);

        for (int i = 0; i < trappedDupes.Count; i++)
        {
            Vector2Int destinationTile = (i < availableNeighbors.Count) ? availableNeighbors[i] : targetTile + Vector2Int.up;
            EjectDuplicantToTile(trappedDupes[i], destinationTile);
        }
    }

    private static void EjectDuplicantToTile(DuplicantController dupe, Vector2Int safeTile)
    {
        float cs = GridManager.Instance.cellSize;
        Vector3 newWorldPos = new Vector3(safeTile.x * cs + cs / 2f, safeTile.y * cs, 0f);

        dupe.transform.position = newWorldPos;
        dupe.gridPosition = safeTile;

        // Reseta o estado atual para interromper qualquer movimento que o estivesse arrastando para dentro do bloco
        if (dupe.currentState == DuplicantController.WorkerState.Moving)
        {
            dupe.StopAllCoroutines();
            dupe.currentState = DuplicantController.WorkerState.Idle;
            dupe.currentTask = null;
        }

        // Caso tenha sido jogado no ar, aciona a queda livre
        DuplicantMovement movement = dupe.Movement;
        if (movement != null && movement.ShouldFall())
        {
            dupe.StartCoroutine(movement.HandleFallingRoutine());
        }
    }

    private static List<Vector2Int> GetAvailableNeighborTiles(Vector2Int center, int requiredCount)
    {
        List<Vector2Int> safeList = new List<Vector2Int>();
        Vector2Int[] searchOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 1),   // Cima
            new Vector2Int(-1, 0),  // Esquerda
            new Vector2Int(1, 0),   // Direita
            new Vector2Int(-1, 1),  // Diagonal Sup Esquerda
            new Vector2Int(1, 1),   // Diagonal Sup Direita
            new Vector2Int(0, -1)   // Baixo
        };

        foreach (var offset in searchOffsets)
        {
            Vector2Int candidate = center + offset;
            if (GridManager.Instance.IsPassable(candidate.x, candidate.y))
            {
                safeList.Add(candidate);
                if (safeList.Count >= requiredCount) break;
            }
        }

        return safeList;
    }
}