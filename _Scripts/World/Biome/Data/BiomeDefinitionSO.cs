using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BiomeTerrainWeight
{
    public TileType tileType = TileType.Stone;
    [Min(0f)] public float weight = 1f;
}

[Serializable]
public sealed class BiomeOreRule
{
    public TileType tileType = TileType.Copper;
    [Min(0)] public int minDepth;
    [Min(0)] public int maxDepth = 500;
    [Min(0.0001f)] public float frequency = 0.12f;
    [Range(0f, 1f)] public float threshold = 0.75f;
}

[Serializable]
public sealed class BiomeFloraRule
{
    [Tooltip("Mantido para compatibilidade com assets criados no P5C.")]
    public string floraId = "flora.new";
    public FloraDefinitionSO definition;
    [Min(0f)] public float density = 0.02f;
    [Min(1)] public int minimumPatchSize = 2;
    [Min(1)] public int maximumPatchSize = 6;
    [Min(0)] public int minimumDepth;
    [Min(0)] public int maximumDepth = 500;
    [Min(0)] public int maximumInstances;
    public bool requireDryCell = true;
    public List<TileType> allowedGround = new List<TileType>();

    public string EffectiveFloraId => definition != null
        ? definition.floraId
        : floraId;

    public bool SupportsDepth(int depth)
    {
        return depth >= minimumDepth && depth <= maximumDepth;
    }

    public bool SupportsGround(TileType terrain)
    {
        return allowedGround == null
            || allowedGround.Count == 0
            || allowedGround.Contains(terrain);
    }
}

[Serializable]
public sealed class BiomeCreatureRule
{
    [Tooltip("Definição da criatura gerada neste bioma.")]
    public CreatureDefinitionSO definition;
    [Tooltip("Chance de uma posição navegável participar do sorteio.")]
    [Range(0f, 1f)] public float spawnChancePerCandidate = 0.02f;
    [Min(0)] public int minimumInstances = 1;
    [Min(0)] public int maximumInstances = 4;
    [Min(0)] public int minimumDepth;
    [Min(0)] public int maximumDepth = 500;
    [Min(0)] public int minimumDistanceFromWorldSpawn = 12;
    [Min(0)] public int minimumSeparation = 6;
    public List<TileType> allowedGround = new List<TileType>();

    public bool SupportsDepth(int depth)
        => depth >= minimumDepth && depth <= maximumDepth;

    public bool SupportsGround(TileType terrain)
        => allowedGround == null || allowedGround.Count == 0
            || allowedGround.Contains(terrain);
}

[Serializable]
public sealed class BiomeEnvironmentParameters
{
    [Range(0f, 1f)] public float targetTemperature = 0.5f;
    [Range(0f, 1f)] public float targetHumidity = 0.5f;
    public float ambientTemperature = 20f;
    [Range(0f, 1f)] public float ambientHumidity = 0.5f;
    public Color ambientTint = Color.white;
}

[CreateAssetMenu(
    fileName = "NewBiomeDefinition",
    menuName = "World/Biomes/Biome Definition")]
public sealed class BiomeDefinitionSO : ScriptableObject
{
    [Header("Identificação")]
    public string biomeId = "biome.new";
    public string displayName = "Novo Bioma";

    [Header("Faixa de profundidade")]
    [Min(0)] public int minimumDepth;
    [Min(0)] public int maximumDepth = 500;
    [Min(0.01f)] public float selectionWeight = 1f;

    [Header("Terreno")]
    public TileType fallbackTerrain = TileType.Stone;
    public TileType surfaceTerrain = TileType.Grass;
    public List<BiomeTerrainWeight> terrainWeights =
        new List<BiomeTerrainWeight>();

    [Header("Cavernas")]
    [Min(0.0001f)] public float caveScale = 0.07f;
    [Range(0f, 1f)] public float caveThreshold = 0.47f;
    [Min(0)] public int caveMinimumDepth = 3;

    [Header("Minérios")]
    public List<BiomeOreRule> ores = new List<BiomeOreRule>();

    [Header("Conteúdo futuro")]
    public List<BiomeFloraRule> flora = new List<BiomeFloraRule>();
    public List<BiomeCreatureRule> creatures =
        new List<BiomeCreatureRule>();

    [Header("Ambiente")]
    public BiomeEnvironmentParameters environment =
        new BiomeEnvironmentParameters();

    public bool SupportsDepth(int depth)
    {
        return depth >= minimumDepth && depth <= maximumDepth;
    }

    public TileType SelectTerrain(float normalizedSample)
    {
        if (terrainWeights == null || terrainWeights.Count == 0)
        {
            return fallbackTerrain;
        }

        float totalWeight = 0f;
        for (int i = 0; i < terrainWeights.Count; i++)
        {
            BiomeTerrainWeight entry = terrainWeights[i];
            if (entry != null) totalWeight += Mathf.Max(0f, entry.weight);
        }

        if (totalWeight <= 0f) return fallbackTerrain;

        float target = Mathf.Clamp01(normalizedSample) * totalWeight;
        for (int i = 0; i < terrainWeights.Count; i++)
        {
            BiomeTerrainWeight entry = terrainWeights[i];
            if (entry == null) continue;
            target -= Mathf.Max(0f, entry.weight);
            if (target <= 0f) return entry.tileType;
        }

        return fallbackTerrain;
    }

    public bool IsBaseTerrain(TileType tileType)
    {
        if (tileType == fallbackTerrain) return true;
        if (terrainWeights == null) return false;

        for (int i = 0; i < terrainWeights.Count; i++)
        {
            BiomeTerrainWeight entry = terrainWeights[i];
            if (entry != null && entry.tileType == tileType) return true;
        }

        return false;
    }

    private void OnValidate()
    {
        biomeId = string.IsNullOrWhiteSpace(biomeId)
            ? name.Trim().ToLowerInvariant().Replace(' ', '.')
            : biomeId.Trim();
        maximumDepth = Mathf.Max(minimumDepth, maximumDepth);
    }
}
