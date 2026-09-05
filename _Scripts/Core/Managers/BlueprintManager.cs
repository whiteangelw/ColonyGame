using UnityEngine;
using System.Collections.Generic;

public class BlueprintManager : Singleton<BlueprintManager>
{
    [Header("Prefab Base do Blueprint")]
    [SerializeField] private GameObject blueprintPrefab;

    private readonly List<ConstructionBlueprint> activeBlueprints =
        new List<ConstructionBlueprint>();

    public bool CanCreateBlueprint(TileType buildTile)
    {
        if (buildTile != TileType.PrintingPod)
        {
            return true;
        }

        return PrintingPod.Instance == null
            && !HasActiveBlueprint(TileType.PrintingPod);
    }

    public bool HasActiveBlueprint(TileType tileType)
    {
        activeBlueprints.RemoveAll(blueprint => blueprint == null);

        return activeBlueprints.Exists(
            blueprint => blueprint.targetTileType == tileType
        );
    }

    public void UnregisterBlueprint(ConstructionBlueprint blueprint)
    {
        if (blueprint != null)
        {
            activeBlueprints.Remove(blueprint);
        }
    }

    public List<ConstructionBlueprint> GetActiveBlueprints()
    {
        activeBlueprints.RemoveAll(blueprint => blueprint == null);
        return new List<ConstructionBlueprint>(activeBlueprints);
    }

    public void ClearBlueprintsForLoad()
    {
        for (int i = activeBlueprints.Count - 1; i >= 0; i--)
        {
            ConstructionBlueprint blueprint = activeBlueprints[i];

            if (blueprint != null)
            {
                blueprint.gameObject.SetActive(false);
                Destroy(blueprint.gameObject);
            }
        }

        activeBlueprints.Clear();
    }

    /// <summary>
    /// Cria a entidade Blueprint no mapa, configurando os custos e a carga de trabalho.
    /// </summary>
    public ConstructionBlueprint CreateBlueprint(Vector2Int gridPos, TileType buildTile)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[BlueprintManager] GridManager não encontrado.");
            return null;
        }

        if (blueprintPrefab == null)
        {
            Debug.LogError(
                "[BlueprintManager] Blueprint Prefab não foi configurado no Inspector."
            );
            return null;
        }

        if (!CanCreateBlueprint(buildTile))
        {
            Debug.LogWarning(
                "[BlueprintManager] Já existe um PrintingPod ou um blueprint dele."
            );
            return null;
        }

        float cs = GridManager.Instance.cellSize;
        Vector3 spawnPos = new Vector3(gridPos.x * cs + cs / 2f, gridPos.y * cs + cs / 2f, 0);

        GameObject bpObj = Instantiate(blueprintPrefab, spawnPos, Quaternion.identity);
        ConstructionBlueprint bp = bpObj.GetComponent<ConstructionBlueprint>();

        if (bp == null)
        {
            Debug.LogError(
                "[BlueprintManager] O prefab não possui ConstructionBlueprint."
            );
            Destroy(bpObj);
            return null;
        }

        ResourceType reqResource = BuildingCosts.GetRequiredResource(buildTile);
        int cost = BuildingCosts.GetCost(buildTile);
        if (cost <= 0) cost = 1; // Safeguard caso o custo não esteja mapeado

        float workTime = 3f;

        bp.Initialize(gridPos, buildTile, reqResource, cost, workTime);
        activeBlueprints.Add(bp);
        return bp;
    }

    public ConstructionBlueprint RestoreBlueprint(
        Vector2Int gridPos,
        TileType buildTile,
        ResourceType resource,
        int requiredAmount,
        float totalWorkRequired,
        int deliveredAmount,
        float currentWorkDone)
    {
        if (blueprintPrefab == null || GridManager.Instance == null)
        {
            return null;
        }

        GameObject blueprintObject = Instantiate(
            blueprintPrefab,
            GridManager.Instance.GridToWorldPosition(gridPos),
            Quaternion.identity
        );

        ConstructionBlueprint blueprint =
            blueprintObject.GetComponent<ConstructionBlueprint>();

        if (blueprint == null)
        {
            Destroy(blueprintObject);
            return null;
        }

        blueprint.Initialize(
            gridPos,
            buildTile,
            resource,
            Mathf.Max(1, requiredAmount),
            Mathf.Max(0.01f, totalWorkRequired)
        );
        blueprint.RestoreProgress(deliveredAmount, currentWorkDone);
        activeBlueprints.Add(blueprint);
        return blueprint;
    }
}
