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
}
