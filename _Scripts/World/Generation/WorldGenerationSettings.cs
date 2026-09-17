using System.Collections.Generic;
using System;

/// <summary>
/// Entrada imutável de uma execução da geração legada.
/// </summary>
public sealed class WorldGenerationSettings
{
    public int Width { get; }
    public int Height { get; }
    public float Seed { get; }
    public int GenerationVersion { get; }
    public float SurfaceFrequency { get; }
    public int SurfaceHeight { get; }
    public int HeightVariation { get; }
    public float RelativeSurfaceHeight { get; }
    public int MinimumSkyHeight { get; }
    public int MinimumTerrainDepth { get; }
    public IReadOnlyList<WorldBiomeLayer> BiomeLayers { get; }
    public BiomeCatalogSO BiomeCatalog { get; }
    public int NumberOfLakes { get; }
    public int LakeWidth { get; }
    public int LakeDepth { get; }

    public WorldGenerationSettings(
        int width,
        int height,
        float seed,
        int generationVersion,
        float surfaceFrequency,
        int surfaceHeight,
        int heightVariation,
        float relativeSurfaceHeight,
        int minimumSkyHeight,
        int minimumTerrainDepth,
        IReadOnlyList<WorldBiomeLayer> biomeLayers,
        BiomeCatalogSO biomeCatalog,
        int numberOfLakes,
        int lakeWidth,
        int lakeDepth)
    {
        Width = width;
        Height = height;
        Seed = seed;
        GenerationVersion = generationVersion;
        SurfaceFrequency = surfaceFrequency;
        SurfaceHeight = surfaceHeight;
        HeightVariation = heightVariation;
        RelativeSurfaceHeight = relativeSurfaceHeight;
        MinimumSkyHeight = minimumSkyHeight;
        MinimumTerrainDepth = minimumTerrainDepth;
        BiomeLayers = biomeLayers;
        BiomeCatalog = biomeCatalog;
        NumberOfLakes = numberOfLakes;
        LakeWidth = lakeWidth;
        LakeDepth = lakeDepth;
    }

    public int ResolveBaseSurfaceHeight()
    {
        if (GenerationVersion < 4) return SurfaceHeight;

        float ratio = Math.Max(0.10f, Math.Min(0.90f, RelativeSurfaceHeight));
        int variationReserve = Math.Max(0, HeightVariation);
        int skyReserve = Math.Max(2, MinimumSkyHeight);
        int maximumSurface = Math.Max(
            1,
            Height - 1 - skyReserve - variationReserve);
        int minimumSurface = Math.Min(
            maximumSurface,
            Math.Max(1, MinimumTerrainDepth));
        int proportionalSurface = (int)Math.Round(
            (Height - 1) * ratio,
            MidpointRounding.AwayFromZero);

        return Math.Max(
            minimumSurface,
            Math.Min(maximumSurface, proportionalSurface));
    }
}
