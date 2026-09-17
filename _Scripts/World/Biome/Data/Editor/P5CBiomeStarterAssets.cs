#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class P5CBiomeStarterAssets
{
    private const string Root = "Assets/World/Biomes/P5C";

    [MenuItem("Tools/World/P5C/Create Starter Biomes")]
    public static void CreateStarterBiomes()
    {
        EnsureFolder("Assets", "World");
        EnsureFolder("Assets/World", "Biomes");
        EnsureFolder("Assets/World/Biomes", "P5C");

        BiomeDefinitionSO temperate = CreateBiome(
            "TemperateSurface",
            "biome.temperate_surface",
            "Superfície Temperada",
            0,
            34,
            TileType.Solid,
            TileType.Grass,
            0.52f,
            0.58f,
            new[]
            {
                Terrain(TileType.Solid, 8f),
                Terrain(TileType.Stone, 2f)
            },
            new[]
            {
                Ore(TileType.Copper, 8, 34, 0.10f, 0.76f),
                Ore(TileType.Coal, 12, 34, 0.075f, 0.80f)
            });

        BiomeDefinitionSO fungal = CreateBiome(
            "FungalDepths",
            "biome.fungal_depths",
            "Profundezas Fúngicas",
            24,
            110,
            TileType.Stone,
            TileType.Stone,
            0.42f,
            0.82f,
            new[]
            {
                Terrain(TileType.Stone, 7f),
                Terrain(TileType.Solid, 3f)
            },
            new[]
            {
                Ore(TileType.Coal, 24, 110, 0.08f, 0.77f),
                Ore(TileType.Copper, 30, 90, 0.11f, 0.82f)
            });

        BiomeDefinitionSO rocky = CreateBiome(
            "RockyDepths",
            "biome.rocky_depths",
            "Profundezas Rochosas",
            55,
            10000,
            TileType.Stone,
            TileType.Stone,
            0.36f,
            0.32f,
            new[]
            {
                Terrain(TileType.Stone, 9f),
                Terrain(TileType.Solid, 1f)
            },
            new[]
            {
                Ore(TileType.Iron, 55, 10000, 0.09f, 0.79f),
                Ore(TileType.Gold, 90, 10000, 0.07f, 0.86f)
            });

        string catalogPath = Root + "/StarterBiomeCatalog.asset";
        BiomeCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BiomeCatalogSO>(
            catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<BiomeCatalogSO>();
            AssetDatabase.CreateAsset(catalog, catalogPath);
        }

        catalog.regionFrequency = 0.0125f;
        catalog.fallbackBiome = temperate;
        catalog.biomes = new List<BiomeDefinitionSO>
        {
            temperate,
            fungal,
            rocky
        };
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
    }

    private static BiomeDefinitionSO CreateBiome(
        string assetName,
        string id,
        string displayName,
        int minimumDepth,
        int maximumDepth,
        TileType fallbackTerrain,
        TileType surfaceTerrain,
        float temperature,
        float humidity,
        BiomeTerrainWeight[] terrain,
        BiomeOreRule[] ores)
    {
        string path = Root + "/" + assetName + ".asset";
        BiomeDefinitionSO biome =
            AssetDatabase.LoadAssetAtPath<BiomeDefinitionSO>(path);
        if (biome == null)
        {
            biome = ScriptableObject.CreateInstance<BiomeDefinitionSO>();
            AssetDatabase.CreateAsset(biome, path);
        }

        biome.biomeId = id;
        biome.displayName = displayName;
        biome.minimumDepth = minimumDepth;
        biome.maximumDepth = maximumDepth;
        biome.fallbackTerrain = fallbackTerrain;
        biome.surfaceTerrain = surfaceTerrain;
        biome.terrainWeights = new List<BiomeTerrainWeight>(terrain);
        biome.ores = new List<BiomeOreRule>(ores);
        if (biome.environment == null)
        {
            biome.environment = new BiomeEnvironmentParameters();
        }
        biome.environment.targetTemperature = temperature;
        biome.environment.targetHumidity = humidity;
        EditorUtility.SetDirty(biome);
        return biome;
    }

    private static BiomeTerrainWeight Terrain(TileType type, float weight)
    {
        return new BiomeTerrainWeight { tileType = type, weight = weight };
    }

    private static BiomeOreRule Ore(
        TileType type,
        int minDepth,
        int maxDepth,
        float frequency,
        float threshold)
    {
        return new BiomeOreRule
        {
            tileType = type,
            minDepth = minDepth,
            maxDepth = maxDepth,
            frequency = frequency,
            threshold = threshold
        };
    }

    private static void EnsureFolder(string parent, string folder)
    {
        string path = parent + "/" + folder;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif
