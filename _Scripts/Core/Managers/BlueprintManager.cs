using UnityEngine;

public class BlueprintManager : Singleton<BlueprintManager>
{
    [Header("Prefab Base do Blueprint")]
    [SerializeField] private GameObject blueprintPrefab;

    /// <summary>
    /// Cria a entidade Blueprint no mapa, configurando os custos e a carga de trabalho.
    /// </summary>
    public ConstructionBlueprint CreateBlueprint(Vector2Int gridPos, TileType buildTile)
    {
        if (GridManager.Instance == null || blueprintPrefab == null) return null;

        float cs = GridManager.Instance.cellSize;
        Vector3 spawnPos = new Vector3(gridPos.x * cs + cs / 2f, gridPos.y * cs + cs / 2f, 0);

        GameObject bpObj = Instantiate(blueprintPrefab, spawnPos, Quaternion.identity);
        ConstructionBlueprint bp = bpObj.GetComponent<ConstructionBlueprint>();

        ResourceType reqResource = BuildingCosts.GetRequiredResource(buildTile);
        int cost = BuildingCosts.GetCost(buildTile);
        if (cost <= 0) cost = 1; // Safeguard caso o custo não esteja mapeado

        float workTime = 3f;

        bp.Initialize(gridPos, buildTile, reqResource, cost, workTime);
        return bp;
    }
}