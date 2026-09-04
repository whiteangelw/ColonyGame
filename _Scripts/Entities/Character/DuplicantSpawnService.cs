using System.Collections.Generic;
using UnityEngine;

public class DuplicantSpawnService : MonoBehaviour
{
    [Header("Catálogo de duplicants")]
    [SerializeField] private List<DuplicantDefinition> definitions =
        new List<DuplicantDefinition>();

    [Tooltip("Usado somente enquanto o catálogo de definições estiver vazio.")]
    [SerializeField] private GameObject fallbackPrefab;

    [Header("Busca por posição segura")]
    [Min(1)]
    [SerializeField] private int safePositionSearchRadius = 8;

    public List<DuplicantController> SpawnRandomGroup(
        Vector2Int center,
        int amount)
    {
        List<DuplicantController> spawned =
            new List<DuplicantController>();

        if (amount <= 0 || GridManager.Instance == null)
        {
            return spawned;
        }

        List<Vector2Int> safePositions = FindSafePositions(center, amount);

        for (int i = 0; i < safePositions.Count; i++)
        {
            DuplicantController duplicant = SpawnRandom(safePositions[i]);

            if (duplicant != null)
            {
                spawned.Add(duplicant);
            }
        }

        if (spawned.Count < amount)
        {
            Debug.LogError(
                "[DuplicantSpawnService] Não foi possível criar todos os " +
                "duplicants iniciais em posições seguras."
            );
        }

        return spawned;
    }

    public DuplicantController SpawnRandom(Vector2Int gridPosition)
    {
        DuplicantDefinition definition = ChooseRandomDefinition();
        GameObject prefab = definition != null
            ? definition.prefab
            : fallbackPrefab;

        if (prefab == null)
        {
            Debug.LogError(
                "[DuplicantSpawnService] Nenhum prefab de duplicant configurado."
            );
            return null;
        }

        GridManager grid = GridManager.Instance;

        if (grid == null || !grid.IsStandable(gridPosition))
        {
            Debug.LogWarning(
                "[DuplicantSpawnService] Posição de spawn inválida: " +
                gridPosition
            );
            return null;
        }

        GameObject instance = Instantiate(
            prefab,
            grid.GridToWorldPosition(gridPosition),
            Quaternion.identity
        );

        DuplicantController controller =
            instance.GetComponent<DuplicantController>();

        if (controller == null)
        {
            Debug.LogError(
                "[DuplicantSpawnService] O prefab não possui DuplicantController."
            );
            Destroy(instance);
            return null;
        }

        if (definition != null)
        {
            if (definition.workProfile != null)
            {
                controller.workProfile = definition.workProfile;
            }

            if (definition.capabilityProfile != null)
            {
                controller.capabilityProfile = definition.capabilityProfile;
            }

            if (!string.IsNullOrWhiteSpace(definition.displayName))
            {
                instance.name = definition.displayName;
            }
        }

        return controller;
    }

    private DuplicantDefinition ChooseRandomDefinition()
    {
        float totalWeight = 0f;

        for (int i = 0; i < definitions.Count; i++)
        {
            DuplicantDefinition definition = definitions[i];

            if (definition != null && definition.prefab != null)
            {
                totalWeight += Mathf.Max(0.01f, definition.selectionWeight);
            }
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float roll = Random.value * totalWeight;

        for (int i = 0; i < definitions.Count; i++)
        {
            DuplicantDefinition definition = definitions[i];

            if (definition == null || definition.prefab == null)
            {
                continue;
            }

            roll -= Mathf.Max(0.01f, definition.selectionWeight);

            if (roll <= 0f)
            {
                return definition;
            }
        }

        return null;
    }

    private List<Vector2Int> FindSafePositions(
        Vector2Int center,
        int amount)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int radius = 0;
             radius <= safePositionSearchRadius && result.Count < amount;
             radius++)
        {
            TryAddSafePosition(center + Vector2Int.left * radius, result);

            if (radius > 0)
            {
                TryAddSafePosition(center + Vector2Int.right * radius, result);
            }
        }

        if (result.Count >= amount)
        {
            return result;
        }

        for (int yOffset = 1;
             yOffset <= safePositionSearchRadius && result.Count < amount;
             yOffset++)
        {
            for (int xOffset = -safePositionSearchRadius;
                 xOffset <= safePositionSearchRadius && result.Count < amount;
                 xOffset++)
            {
                TryAddSafePosition(
                    center + new Vector2Int(xOffset, yOffset),
                    result
                );

                TryAddSafePosition(
                    center + new Vector2Int(xOffset, -yOffset),
                    result
                );
            }
        }

        return result;
    }

    private void TryAddSafePosition(
        Vector2Int candidate,
        List<Vector2Int> result)
    {
        GridManager grid = GridManager.Instance;

        if (grid != null
            && grid.IsStandable(candidate)
            && !result.Contains(candidate))
        {
            result.Add(candidate);
        }
    }
}
