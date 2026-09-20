using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainCatalogService : Singleton<TerrainCatalogService>
{
    [SerializeField] private TerrainCatalogSO catalog;

    private readonly Dictionary<TileType, TerrainDefinitionSO> byTileType =
        new Dictionary<TileType, TerrainDefinitionSO>();
    private readonly HashSet<TileType> missingDefinitionWarnings =
        new HashSet<TileType>();
    private bool initialized;

    protected override void Awake()
    {
        base.Awake();
        RebuildIndex();
    }

    public TerrainDefinitionSO GetByTileType(TileType tileType)
    {
        EnsureInitialized();
        byTileType.TryGetValue(tileType, out TerrainDefinitionSO definition);
        return definition;
    }

    public bool TryGetDefinition(
        TileType tileType,
        out TerrainDefinitionSO definition)
    {
        definition = GetByTileType(tileType);
        return definition != null;
    }

    public void WarnMissingDefinition(TileType tileType)
    {
        if (!missingDefinitionWarnings.Add(tileType)) return;

        Debug.LogWarning(
            $"[TerrainCatalogService] Nenhuma TerrainDefinitionSO foi " +
            $"cadastrada para {tileType}. O terreno será removido sem drop.");
    }

    public void RebuildIndex()
    {
        initialized = true;
        byTileType.Clear();
        missingDefinitionWarnings.Clear();

        if (catalog == null)
        {
            Debug.LogError(
                "[TerrainCatalogService] TerrainCatalogSO não configurado.",
                this);
            return;
        }

        foreach (TerrainDefinitionSO definition in catalog.Definitions)
        {
            if (definition == null) continue;

            if (byTileType.ContainsKey(definition.TileType))
            {
                Debug.LogError(
                    $"[TerrainCatalogService] TileType duplicado: " +
                    $"{definition.TileType}.",
                    definition);
                continue;
            }

            byTileType.Add(definition.TileType, definition);
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized) RebuildIndex();
    }
}
