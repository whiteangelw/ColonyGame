using System.Collections.Generic;
using UnityEngine;

internal sealed class BaseTerrainPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.BaseTerrain;

    public void Execute(WorldGenerationContext context)
    {
        WorldGenerationSettings settings = context.Settings;
        int baseSurfaceHeight = settings.ResolveBaseSurfaceHeight();

        for (int x = 0; x < context.Width; x++)
        {
            float noiseValue = Mathf.PerlinNoise(
                (x + settings.Seed) * settings.SurfaceFrequency,
                settings.Seed);
            int surfaceHeight = baseSurfaceHeight
                + Mathf.FloorToInt(noiseValue * settings.HeightVariation);

            context.SetSurfaceHeight(x, surfaceHeight);

            for (int y = 0; y < context.Height; y++)
            {
                bool isBelowSurface = y <= surfaceHeight;
                context.Result.SetCell(x, y, new WorldCellData
                {
                    TerrainType = isBelowSurface
                        ? TileType.Solid
                        : TileType.Empty,
                    FogState = y >= surfaceHeight
                        ? FogState.Revealed
                        : FogState.Unexplored,
                    LiquidAmount = 0f
                });
            }
        }
    }
}

internal sealed class SurfacePass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Surface;

    // Contrato reservado para regras de perfil de superfície que não devem
    // ficar misturadas com o preenchimento base.
    public void Execute(WorldGenerationContext context) { }
}

internal sealed class LayersPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Layers;

    public void Execute(WorldGenerationContext context)
    {
        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                int depth = surfaceHeight - y;
                BiomeData biome = context.GetBiomeAtDepth(depth);
                context.Result.SetTerrain(
                    x,
                    y,
                    biome != null ? biome.fillTile : TileType.Solid);
            }
        }
    }
}

internal sealed class CavesPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Caves;

    public void Execute(WorldGenerationContext context)
    {
        WorldGenerationSettings settings = context.Settings;

        for (int x = 0; x < context.Width; x++)
        {
            int surfaceHeight = context.GetSurfaceHeight(x);
            int highestY = Mathf.Min(surfaceHeight, context.Height - 1);

            for (int y = 0; y <= highestY; y++)
            {
                int depth = surfaceHeight - y;
                if (depth <= 2) continue;

                BiomeData biome = context.GetBiomeAtDepth(depth);
                if (biome == null) continue;

                float caveNoise = Mathf.PerlinNoise(
                    (x + settings.Seed) * biome.caveScale,
                    (y + settings.Seed) * biome.caveScale);

                if (caveNoise < biome.caveThreshold)
                {
                    context.Result.SetTerrain(x, y, TileType.Empty);
                }
            }
        }
    }
}

/// <summary>
/// Ponto de extensão reservado ao P5C. Não altera células no P5B.
/// </summary>
internal sealed class BiomesPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Biomes;
    public void Execute(WorldGenerationContext context) { }
}

internal sealed class OresPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Ores;

    public void Execute(WorldGenerationContext context)
    {
        WorldGenerationSettings settings = context.Settings;

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
                BiomeData biome = context.GetBiomeAtDepth(depth);
                if (biome == null || biome.ores == null) continue;

                foreach (OreConfig ore in biome.ores)
                {
                    if (ore == null
                        || depth < ore.minDepth
                        || depth > ore.maxDepth)
                    {
                        continue;
                    }

                    float oreOffset = (int)ore.tileType * 1000f;
                    float oreNoise = Mathf.PerlinNoise(
                        (x + settings.Seed + oreOffset) * ore.frequency,
                        (y + settings.Seed + oreOffset) * ore.frequency);

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

internal sealed class SurfaceFeaturesPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.SurfaceFeatures;

    public void Execute(WorldGenerationContext context)
    {
        WorldGenerationSettings settings = context.Settings;
        if (settings.NumberOfLakes <= 0) return;

        DeterministicRandom random = context.CreateRandom(Id);
        int minX = 5;
        int maxX = context.Width - 5;
        int step = (maxX - minX) / (settings.NumberOfLakes + 1);

        for (int i = 1; i <= settings.NumberOfLakes; i++)
        {
            int lakeCenterX = minX + (step * i) + random.NextInt(-2, 3);
            if (Mathf.Abs(lakeCenterX - (context.Width / 2)) < 8)
            {
                continue;
            }

            int topY = WorldGenerationPassUtility.FindTopTerrainY(
                context.Result,
                lakeCenterX);
            int halfWidth = settings.LakeWidth / 2;

            for (int x = lakeCenterX - halfWidth;
                 x <= lakeCenterX + halfWidth;
                 x++)
            {
                if (x <= 1 || x >= context.Width - 2) continue;

                int distanceFromCenter = Mathf.Abs(x - lakeCenterX);
                int depthAtX = settings.LakeDepth - Mathf.FloorToInt(
                    (float)distanceFromCenter / halfWidth
                    * settings.LakeDepth);

                for (int depth = 0; depth < depthAtX; depth++)
                {
                    int targetY = topY - depth;
                    context.Result.SetTerrain(x, targetY, TileType.Empty);
                    context.Result.SetLiquid(x, targetY, 0.95f);
                }

                int bottomY = topY - depthAtX;
                if (bottomY >= 0
                    && context.Result.GetCell(x, bottomY).TerrainType
                        == TileType.Empty)
                {
                    context.Result.SetTerrain(x, bottomY, TileType.Solid);
                }
            }
        }
    }
}

internal sealed class FloraPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.Flora;

    public void Execute(WorldGenerationContext context)
    {
        // A versão 1 continua exatamente sem flora procedural.
        if (context.Settings.GenerationVersion < 3) return;

        Dictionary<BiomeFloraRule, int> instanceCounts =
            new Dictionary<BiomeFloraRule, int>();

        for (int x = 1; x < context.Width - 1; x++)
        {
            for (int y = 1; y < context.Height - 1; y++)
            {
                BiomeDefinitionSO biome;
                TileType ground;
                int depth;
                if (!TryGetPlantableCell(
                    context,
                    x,
                    y,
                    null,
                    null,
                    out biome,
                    out ground,
                    out depth))
                {
                    continue;
                }

                List<BiomeFloraRule> rules = biome.flora;
                if (rules == null) continue;

                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    BiomeFloraRule rule = rules[ruleIndex];
                    if (!CanUseRule(rule, ground, depth, instanceCounts))
                    {
                        continue;
                    }

                    int ruleSeed = WorldGenerationSeed.Derive(
                        context.Settings.Seed,
                        context.Settings.GenerationVersion,
                        Id + ":" + rule.EffectiveFloraId);
                    float centerSample = WorldGenerationSeed.Sample01(
                        ruleSeed,
                        x,
                        y,
                        ruleIndex);
                    if (centerSample >= Mathf.Clamp01(rule.density)) continue;

                    PlacePatch(
                        context,
                        biome,
                        rule,
                        new Vector2Int(x, y),
                        ruleSeed,
                        instanceCounts);
                }
            }
        }
    }

    private static void PlacePatch(
        WorldGenerationContext context,
        BiomeDefinitionSO biome,
        BiomeFloraRule rule,
        Vector2Int center,
        int ruleSeed,
        Dictionary<BiomeFloraRule, int> instanceCounts)
    {
        int minimum = Mathf.Max(1, rule.minimumPatchSize);
        int maximum = Mathf.Max(minimum, rule.maximumPatchSize);
        int patchSeed = unchecked(
            ruleSeed ^ center.x * 73856093 ^ center.y * 19349663);
        DeterministicRandom random = new DeterministicRandom(patchSeed);
        int targetCount = random.NextInt(minimum, maximum + 1);
        int attemptsRemaining = targetCount * 8;
        Vector2Int candidate = center;

        while (targetCount > 0 && attemptsRemaining-- > 0)
        {
            BiomeDefinitionSO candidateBiome;
            TileType ground;
            int depth;
            if (TryGetPlantableCell(
                    context,
                    candidate.x,
                    candidate.y,
                    biome,
                    rule,
                    out candidateBiome,
                    out ground,
                    out depth)
                && CanUseRule(rule, ground, depth, instanceCounts)
                && context.Result.TryAddFlora(
                    rule.definition,
                    rule.EffectiveFloraId,
                    candidate.x,
                    candidate.y))
            {
                IncrementCount(rule, instanceCounts);
                targetCount--;
            }

            candidate += RandomCardinalOffset(ref random);
            if (Mathf.Abs(candidate.x - center.x) > maximum
                || Mathf.Abs(candidate.y - center.y) > maximum)
            {
                candidate = center;
            }
        }
    }

    private static bool TryGetPlantableCell(
        WorldGenerationContext context,
        int x,
        int y,
        BiomeDefinitionSO requiredBiome,
        BiomeFloraRule rule,
        out BiomeDefinitionSO biome,
        out TileType ground,
        out int depth)
    {
        biome = null;
        ground = TileType.Empty;
        depth = 0;
        if (x <= 0 || x >= context.Width - 1
            || y <= 1 || y >= context.Height - 1)
        {
            return false;
        }

        WorldCellData cell = context.Result.GetCell(x, y);
        WorldCellData headCell = context.Result.GetCell(x, y + 1);
        WorldCellData groundCell = context.Result.GetCell(x, y - 1);
        if (cell.TerrainType != TileType.Empty
            || headCell.TerrainType != TileType.Empty
            || groundCell.TerrainType == TileType.Empty)
        {
            return false;
        }

        if (rule != null && rule.requireDryCell
            && (cell.LiquidAmount > 0.01f || headCell.LiquidAmount > 0.01f))
        {
            return false;
        }

        biome = context.GetBiome(x, y - 1);
        if (biome == null || (requiredBiome != null && biome != requiredBiome))
        {
            return false;
        }

        ground = groundCell.TerrainType;
        depth = context.GetSurfaceHeight(x) - (y - 1);
        return true;
    }

    private static bool CanUseRule(
        BiomeFloraRule rule,
        TileType ground,
        int depth,
        Dictionary<BiomeFloraRule, int> instanceCounts)
    {
        if (rule == null || rule.definition == null
            || string.IsNullOrWhiteSpace(rule.EffectiveFloraId)
            || rule.density <= 0f
            || !rule.SupportsDepth(depth)
            || !rule.SupportsGround(ground))
        {
            return false;
        }

        return rule.maximumInstances <= 0
            || GetCount(rule, instanceCounts) < rule.maximumInstances;
    }

    private static Vector2Int RandomCardinalOffset(
        ref DeterministicRandom random)
    {
        switch (random.NextInt(0, 4))
        {
            case 0: return Vector2Int.right;
            case 1: return Vector2Int.left;
            case 2: return Vector2Int.up;
            default: return Vector2Int.down;
        }
    }

    private static int GetCount(
        BiomeFloraRule rule,
        Dictionary<BiomeFloraRule, int> counts)
    {
        return counts.TryGetValue(rule, out int count) ? count : 0;
    }

    private static void IncrementCount(
        BiomeFloraRule rule,
        Dictionary<BiomeFloraRule, int> counts)
    {
        counts[rule] = GetCount(rule, counts) + 1;
    }
}

/// <summary>
/// Contrato reservado a conteúdo futuro. Não altera o mundo no P5B.
/// </summary>
internal sealed class PointsOfInterestPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.PointsOfInterest;
    public void Execute(WorldGenerationContext context) { }
}

internal sealed class SpawnValidationPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.SpawnValidation;

    public void Execute(WorldGenerationContext context)
    {
        int centerX = context.Width / 2;
        int surfaceY = WorldGenerationPassUtility.FindTopTerrainY(
            context.Result,
            centerX);
        context.Result.SpawnPosition = new Vector2Int(centerX, surfaceY + 1);
        context.Result.RemoveFloraInArea(
            centerX - 3,
            centerX + 3,
            surfaceY + 1,
            surfaceY + 4);

        for (int x = centerX - 2; x <= centerX + 2; x++)
        {
            for (int y = surfaceY + 1; y <= surfaceY + 4; y++)
            {
                context.Result.SetTerrain(x, y, TileType.Empty);
                context.Result.SetFog(x, y, FogState.Revealed);
                context.Result.SetLiquid(x, y, 0f);
            }

            context.Result.SetLiquid(x, surfaceY, 0f);
            context.Result.SetFog(x, surfaceY, FogState.Revealed);
        }
    }
}

internal sealed class SurfaceCoverPass : IWorldGenerationPass
{
    public string Id => WorldGenerationPassIds.SurfaceCover;

    public void Execute(WorldGenerationContext context)
    {
        WorldGenerationSettings settings = context.Settings;

        for (int x = 0; x < context.Width; x++)
        {
            for (int y = context.Height - 1; y >= 0; y--)
            {
                WorldCellData current = context.Result.GetCell(x, y);
                if (current.TerrainType == TileType.Empty) continue;

                WorldCellData above = context.Result.GetCell(x, y + 1);
                bool isTopExposed = y + 1 >= context.Height
                    || above.TerrainType == TileType.Empty;
                bool hasNoLiquidAbove = y + 1 >= context.Height
                    || above.LiquidAmount <= 0.01f;

                if (isTopExposed && hasNoLiquidAbove)
                {
                    int depth = settings.SurfaceHeight - y;
                    BiomeData biome = context.GetBiomeAtDepth(depth);

                    if (biome != null
                        && current.TerrainType == biome.fillTile)
                    {
                        context.Result.SetTerrain(x, y, biome.surfaceTile);
                    }
                }

                break;
            }
        }
    }
}

internal static class WorldGenerationPassUtility
{
    public static int FindTopTerrainY(WorldGenerationResult result, int x)
    {
        for (int y = result.Height - 1; y >= 0; y--)
        {
            if (result.GetCell(x, y).TerrainType != TileType.Empty)
            {
                return y;
            }
        }

        return 0;
    }
}
