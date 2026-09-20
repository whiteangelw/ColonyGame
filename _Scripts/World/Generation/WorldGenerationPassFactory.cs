using System;

public static class WorldGenerationPassFactory
{
    private static readonly WorldGenerationPipeline Version1 =
        new WorldGenerationPipeline(new IWorldGenerationPass[]
        {
            new BaseTerrainPass(),
            new SurfacePass(),
            new LayersPass(),
            new CavesPass(),
            new BiomesPass(),
            new OresPass(),
            new SurfaceFeaturesPass(),
            new FloraPass(),
            new PointsOfInterestPass(),
            new SpawnValidationPass(),
            new SurfaceCoverPass()
        });

    private static readonly WorldGenerationPipeline Version2 =
        new WorldGenerationPipeline(new IWorldGenerationPass[]
        {
            new BaseTerrainPass(),
            new SurfacePass(),
            new BiomeRegionPassV2(),
            new BiomeTerrainPassV2(),
            new BiomeCavesPassV2(),
            new BiomeOresPassV2(),
            new SurfaceFeaturesPass(),
            new FloraPass(),
            new PointsOfInterestPass(),
            new SpawnValidationPass(),
            new BiomeSurfaceCoverPassV2()
        });

    private static readonly WorldGenerationPipeline Version3 =
        new WorldGenerationPipeline(new IWorldGenerationPass[]
        {
            new BaseTerrainPass(),
            new SurfacePass(),
            new BiomeRegionPassV2(),
            new BiomeTerrainPassV2(),
            new BiomeCavesPassV2(),
            new BiomeOresPassV2(),
            new SurfaceFeaturesPass(),
            new FloraPass(),
            new PointsOfInterestPass(),
            new SpawnValidationPass(),
            new BiomeSurfaceCoverPassV2()
        });

    private static readonly WorldGenerationPipeline Version4 =
        new WorldGenerationPipeline(new IWorldGenerationPass[]
        {
            new BaseTerrainPass(),
            new SurfacePass(),
            new BiomeRegionPassV2(),
            new BiomeTerrainPassV2(),
            new BiomeCavesPassV2(),
            new BiomeOresPassV2(),
            new SurfaceFeaturesPass(),
            new FloraPass(),
            new PointsOfInterestPass(),
            new SpawnValidationPass(),
            new BiomeSurfaceCoverPassV2()
        });

    public static bool IsSupported(int generationVersion)
    {
        return generationVersion == 1
            || generationVersion == 2
            || generationVersion == 3
            || generationVersion == 4;
    }

    public static WorldGenerationPipeline Get(int generationVersion)
    {
        if (generationVersion == 1) return Version1;
        if (generationVersion == 2) return Version2;
        if (generationVersion == 3) return Version3;
        if (generationVersion == 4) return Version4;

        throw new NotSupportedException(
            $"Generation version {generationVersion} não é suportada.");
    }
}
