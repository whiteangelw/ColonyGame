/// <summary>
/// Uma etapa independente da criação do mundo.
/// </summary>
public interface IWorldGenerationPass
{
    string Id { get; }
    void Execute(WorldGenerationContext context);
}

public static class WorldGenerationPassIds
{
    public const string BaseTerrain = "base-terrain";
    public const string Surface = "surface";
    public const string Layers = "layers";
    public const string Caves = "caves";
    public const string Biomes = "biomes";
    public const string Ores = "ores";
    public const string SurfaceFeatures = "surface-features";
    public const string Flora = "flora";
    public const string PointsOfInterest = "points-of-interest";
    public const string SpawnValidation = "spawn-validation";
    public const string SurfaceCover = "surface-cover";
}
