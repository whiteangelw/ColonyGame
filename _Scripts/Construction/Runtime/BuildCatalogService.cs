using System.Collections.Generic;
using UnityEngine;

public sealed class BuildCatalogService : Singleton<BuildCatalogService>
{
    [SerializeField] private BuildCatalogSO catalog;

    private readonly Dictionary<string, BuildDefinitionSO> byId =
        new Dictionary<string, BuildDefinitionSO>(System.StringComparer.Ordinal);
    private bool initialized;

    public IReadOnlyList<BuildDefinitionSO> Definitions
    {
        get
        {
            return catalog != null
                ? catalog.Definitions
                : System.Array.Empty<BuildDefinitionSO>();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        RebuildIndex();
    }

    public BuildDefinitionSO GetById(string definitionId)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(definitionId)) return null;
        byId.TryGetValue(definitionId.Trim(), out BuildDefinitionSO definition);
        return definition;
    }

    public BuildDefinitionSO FindLegacy(TileType tileType, GridLayer layer)
    {
        EnsureInitialized();

        foreach (BuildDefinitionSO definition in Definitions)
        {
            if (definition != null
                && definition.TileType == tileType
                && definition.PlacementLayer == layer)
            {
                return definition;
            }
        }

        return null;
    }

    public void RebuildIndex()
    {
        initialized = true;
        byId.Clear();

        foreach (BuildDefinitionSO definition in Definitions)
        {
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.DefinitionId))
            {
                continue;
            }

            string normalizedId = definition.DefinitionId.Trim();
            if (byId.ContainsKey(normalizedId))
            {
                Debug.LogError(
                    $"[BuildCatalogService] ID duplicado: {normalizedId}",
                    definition);
                continue;
            }

            byId.Add(normalizedId, definition);
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized) RebuildIndex();
    }
}
