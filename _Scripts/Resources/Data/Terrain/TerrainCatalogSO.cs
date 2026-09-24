using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TerrainCatalog",
    menuName = "World/Terrain/Terrain Catalog")]
public sealed class TerrainCatalogSO : ScriptableObject
{
    [SerializeField] private List<TerrainDefinitionSO> definitions =
        new List<TerrainDefinitionSO>();

    public IReadOnlyList<TerrainDefinitionSO> Definitions => definitions;

    private void OnValidate()
    {
        HashSet<TileType> seenTypes = new HashSet<TileType>();
        for (int i = 0; i < definitions.Count; i++)
        {
            TerrainDefinitionSO definition = definitions[i];
            if (definition == null) continue;

            if (!seenTypes.Add(definition.TileType))
            {
                Debug.LogError(
                    $"[TerrainCatalogSO] TileType duplicado: " +
                    $"{definition.TileType}.",
                    this);
            }
        }
    }
}
