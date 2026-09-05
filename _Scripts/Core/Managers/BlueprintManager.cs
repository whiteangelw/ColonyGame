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

    public bool CanCreateBlueprint(
        Vector2Int gridPos,
        TileType buildTile,
        GridLayer layer,
        out string failureReason)
    {
        if (!CanCreateBlueprint(buildTile))
        {
            failureReason = buildTile == TileType.PrintingPod
                ? "Já existe um Printing Pod ou um projeto dele."
                : "Esta construção não pode ser criada agora.";
            return false;
        }

        if (GridManager.Instance == null)
        {
            failureReason = "GridManager não encontrado.";
            return false;
        }

        return GridManager.Instance.CanPlaceFootprint(
            gridPos,
            StructureFootprintSettings.Resolve(buildTile),
            layer,
            out failureReason);
    }

    public bool CanCreateBlueprint(
        Vector2Int gridPos,
        TileType buildTile,
        out string failureReason)
    {
        return CanCreateBlueprint(
            gridPos,
            buildTile,
            GridLayer.Terrain,
            out failureReason);
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

    public bool CancelBlueprintAt(Vector2Int position)
    {
        activeBlueprints.RemoveAll(blueprint => blueprint == null);
        foreach (ConstructionBlueprint blueprint in activeBlueprints.ToArray())
        {
            StructureFootprintDefinition footprint = blueprint.Footprint
                ?? StructureFootprintSettings.Resolve(blueprint.targetTileType);
            Vector2Int min = footprint.GetMinimumCell(blueprint.gridPosition);
            if (position.x >= min.x && position.x < min.x + footprint.width
                && position.y >= min.y && position.y < min.y + footprint.height)
            {
                activeBlueprints.Remove(blueprint);
                Destroy(blueprint.gameObject);
                return true;
            }
        }
        return false;
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
    public ConstructionBlueprint CreateBlueprint(Vector2Int gridPos, TileType buildTile, GridLayer layer = GridLayer.Terrain)
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

        if (!CanCreateBlueprint(gridPos, buildTile, layer, out string placementFailure))
        {
            GameEvents.TriggerFloatingTextRequested(
                placementFailure,
                GridManager.Instance.GridToWorldPosition(gridPos),
                Color.red);
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

        bp.Initialize(gridPos, buildTile, reqResource, cost, workTime, layer);

        if (!bp.TryReserveFootprint())
        {
            Debug.LogError(
                "[BlueprintManager] Não foi possível reservar a área do blueprint.",
                bp);
            Destroy(bpObj);
            return null;
        }

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
        => RestoreBlueprint(gridPos, buildTile, resource, requiredAmount, totalWorkRequired,
            deliveredAmount, currentWorkDone, GridLayer.Terrain);

    public ConstructionBlueprint RestoreBlueprint(
        Vector2Int gridPos, TileType buildTile, ResourceType resource,
        int requiredAmount, float totalWorkRequired, int deliveredAmount,
        float currentWorkDone, GridLayer layer)
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
            Mathf.Max(0.01f, totalWorkRequired),
            layer
        );

        if (!blueprint.TryReserveFootprint())
        {
            Debug.LogError(
                $"[BlueprintManager] O blueprint salvo em {gridPos} conflita com outra ocupação.",
                blueprint);
            Destroy(blueprintObject);
            return null;
        }

        blueprint.RestoreProgress(deliveredAmount, currentWorkDone);
        activeBlueprints.Add(blueprint);
        return blueprint;
    }
}
