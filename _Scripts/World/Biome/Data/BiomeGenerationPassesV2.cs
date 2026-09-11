using System.Collections.Generic;
using UnityEngine;

internal sealed class BiomeRegionPassV2 : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Biomes;

    public void Execute(WorldGenerationContext context)
    {
        BiomeCatalogSO catalog = context.Settings.BiomeCatalog;
        if (catalog == null) return;

        int passSeed = WorldGenerationSeed.Derive(
            context.Settings.Seed,
            context.Settings.GenerationVersion,
            Id);
        uint unsignedSeed = unchecked((uint)passSeed);
        float temperatureOffset = unsignedSeed % 100000u;
        float humidityOffset = (unsignedSeed >> 8) % 100000u;
        float frequency = Mathf.Max(0.0001f, catalog.regionFrequency);

        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                int depth = surfaceHeight - y;
                float temperature = Mathf.PerlinNoise(
                    (x + temperatureOffset) * frequency,
                    (y + humidityOffset) * frequency);
                float humidity = Mathf.PerlinNoise(
                    (x - humidityOffset) * frequency,
                    (y + temperatureOffset) * frequency);

                context.SetBiome(
                    x,
                    y,
                    SelectBiome(catalog, depth, temperature, humidity));
            }
        }
    }

    private static BiomeDefinitionSO SelectBiome(
        BiomeCatalogSO catalog,
        int depth,
        float temperature,
        float humidity)
    {
        IReadOnlyList<BiomeDefinitionSO> biomes = catalog.biomes;
        if (biomes == null || biomes.Count == 0)
        {
            return catalog.fallbackBiome;
        }

        BiomeDefinitionSO selected = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < biomes.Count; i++)
        {
            BiomeDefinitionSO biome = biomes[i];
            if (biome == null || !biome.SupportsDepth(depth)) continue;

            BiomeEnvironmentParameters environment = biome.environment;
            float targetTemperature = environment != null
                ? environment.targetTemperature
                : 0.5f;
            float targetHumidity = environment != null
                ? environment.targetHumidity
                : 0.5f;
            float temperatureDelta = temperature - targetTemperature;
            float humidityDelta = humidity - targetHumidity;
            float climateDistance = temperatureDelta * temperatureDelta
                + humidityDelta * humidityDelta;
            float score = -climateDistance
                + Mathf.Max(0.01f, biome.selectionWeight) * 0.05f;

            if (score > bestScore)
            {
                bestScore = score;
                selected = biome;
            }
        }

        return selected != null ? selected : catalog.fallbackBiome;
    }
}

internal sealed class BiomeTerrainPassV2 : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Layers;

    public void Execute(WorldGenerationContext context)
    {
        int passSeed = WorldGenerationSeed.Derive(
            context.Settings.Seed,
            context.Settings.GenerationVersion,
            Id);

        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                BiomeDefinitionSO biome = context.GetBiome(x, y);
                TileType terrain = biome != null
                    ? biome.SelectTerrain(WorldGenerationSeed.Sample01(
                        passSeed,
                        x,
                        y))
                    : TileType.Solid;
                context.Result.SetTerrain(x, y, terrain);
            }
        }
    }
}

internal sealed class BiomeCavesPassV2 : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Caves;

    public void Execute(WorldGenerationContext context)
    {
        int passSeed = WorldGenerationSeed.Derive(
            context.Settings.Seed,
            context.Settings.GenerationVersion,
            Id);
        float offset = unchecked((uint)passSeed) % 100000u;

        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                int depth = surfaceHeight - y;
                BiomeDefinitionSO biome = context.GetBiome(x, y);
                if (biome == null || depth < biome.caveMinimumDepth) continue;

                float scale = Mathf.Max(0.0001f, biome.caveScale);
                float caveNoise = Mathf.PerlinNoise(
                    (x + offset) * scale,
                    (y + offset) * scale);

                if (caveNoise < biome.caveThreshold)
                {
                    context.Result.SetTerrain(x, y, TileType.Empty);
                }
            }
        }
    }
}

internal sealed class BiomeOresPassV2 : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Ores;

    public void Execute(WorldGenerationContext context)
    {
        int passSeed = WorldGenerationSeed.Derive(
            context.Settings.Seed,
            context.Settings.GenerationVersion,
            Id);

        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                if (context.Result.GetCell(x, y).TerrainType == TileType.Empty)
                {
                    continue;
                }

                int depth = surfaceHeight - y;
                BiomeDefinitionSO biome = context.GetBiome(x, y);
                if (biome == null || biome.ores == null) continue;

                for (int i = 0; i < biome.ores.Count; i++)
                {
                    BiomeOreRule ore = biome.ores[i];
                    if (ore == null
                        || depth < ore.minDepth
                        || depth > ore.maxDepth)
                    {
                        continue;
                    }

                    float offset = WorldGenerationSeed.Sample01(
                        passSeed,
                        i,
                        0,
                        (int)ore.tileType) * 100000f;
                    float frequency = Mathf.Max(0.0001f, ore.frequency);
                    float oreNoise = Mathf.PerlinNoise(
                        (x + offset) * frequency,
                        (y + offset) * frequency);

                    if (oreNoise > ore.threshold)
                    {
                        context.Result.SetTerrain(x, y, ore.tileType);
                        break;
                    }
                }
            }
        }
    }
}

internal sealed class BiomeSurfaceCoverPassV2 : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.SurfaceCover;

    public void Execute(WorldGenerationContext context)
    {
        for (int x = 0; x < context.Width; x++)
        {
            for (int y = context.Height - 1; y >= 0; y--)
            {
                WorldCellData current = context.Result.GetCell(x, y);
                if (current.TerrainType == TileType.Empty) continue;

                WorldCellData above = context.Result.GetCell(x, y + 1);
                bool exposed = y + 1 >= context.Height
                    || above.TerrainType == TileType.Empty;
                bool dryAbove = y + 1 >= context.Height
                    || above.LiquidAmount <= 0.01f;
                BiomeDefinitionSO biome = context.GetBiome(x, y);

                if (exposed
                    && dryAbove
                    && biome != null
                    && biome.IsBaseTerrain(current.TerrainType))
                {
                    context.Result.SetTerrain(x, y, biome.surfaceTerrain);
                }

                break;
            }
        }
    }
}
