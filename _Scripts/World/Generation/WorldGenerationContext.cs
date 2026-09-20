using System.Collections.Generic;

/// <summary>
/// Estado compartilhado somente durante uma execução do pipeline.
/// </summary>
public sealed class WorldGenerationContext
{
    private readonly int[] surfaceHeights;
    private BiomeDefinitionSO[] biomeMap;

    public WorldGenerationSettings Settings { get; }
    public WorldGenerationResult Result { get; }

    public int Width => Settings.Width;
    public int Height => Settings.Height;

    public WorldGenerationContext(WorldGenerationSettings settings)
    {
        Settings = settings;
        Result = new WorldGenerationResult(
            settings.Width,
            settings.Height,
            settings.Seed,
            settings.GenerationVersion);
        surfaceHeights = new int[settings.Width];
    }

    public void SetSurfaceHeight(int x, int height)
    {
        if (x >= 0 && x < surfaceHeights.Length)
        {
            surfaceHeights[x] = height;
        }
    }

    public int GetSurfaceHeight(int x)
    {
        return x >= 0 && x < surfaceHeights.Length
            ? surfaceHeights[x]
            : 0;
    }

    public DeterministicRandom CreateRandom(string passId)
    {
        return new DeterministicRandom(WorldGenerationSeed.Derive(
            Settings.Seed,
            Settings.GenerationVersion,
            passId));
    }

    public void SetBiome(int x, int y, BiomeDefinitionSO biome)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        if (biomeMap == null) biomeMap = new BiomeDefinitionSO[Width * Height];
        biomeMap[x + y * Width] = biome;
        Result.SetBiomeId(x, y, biome != null ? biome.biomeId : null);
    }

    public BiomeDefinitionSO GetBiome(int x, int y)
    {
        if (biomeMap == null
            || x < 0 || x >= Width
            || y < 0 || y >= Height)
        {
            return null;
        }

        return biomeMap[x + y * Width];
    }

    public BiomeData GetBiomeAtDepth(int depth)
    {
        IReadOnlyList<WorldBiomeLayer> layers = Settings.BiomeLayers;
        BiomeData activeBiome = layers[0].biome;

        for (int i = 0; i < layers.Count; i++)
        {
            WorldBiomeLayer layer = layers[i];
            if (layer != null && depth >= layer.startDepth)
            {
                activeBiome = layer.biome;
            }
        }

        return activeBiome;
    }
}
