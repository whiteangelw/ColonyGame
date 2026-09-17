/// <summary>
/// Fachada compatível com o P5A. A implementação agora é um pipeline de passes.
/// </summary>
public static class LegacyWorldGeneration
{
    public static WorldGenerationResult Generate(
        WorldGenerationSettings settings)
    {
        return WorldGenerationPassFactory
            .Get(settings.GenerationVersion)
            .Generate(settings);
    }
}
