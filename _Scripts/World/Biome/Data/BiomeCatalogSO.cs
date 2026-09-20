using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NewBiomeCatalog",
    menuName = "World/Biomes/Biome Catalog")]
public sealed class BiomeCatalogSO : ScriptableObject
{
    [Min(0.0001f)] public float regionFrequency = 0.0125f;
    public BiomeDefinitionSO fallbackBiome;
    public List<BiomeDefinitionSO> biomes = new List<BiomeDefinitionSO>();

    public bool IsConfigured => fallbackBiome != null
        || (biomes != null && biomes.Count > 0);
}
