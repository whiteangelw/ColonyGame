using System;
using System.Collections.Generic;

/// <summary>
/// Executa passes ordenados. O pipeline não conhece MonoBehaviours.
/// </summary>
public sealed class WorldGenerationPipeline
{
    private readonly IReadOnlyList<IWorldGenerationPass> passes;

    public WorldGenerationPipeline(IReadOnlyList<IWorldGenerationPass> passes)
    {
        this.passes = passes ?? throw new ArgumentNullException(nameof(passes));
    }

    public WorldGenerationResult Generate(WorldGenerationSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        WorldGenerationContext context = new WorldGenerationContext(settings);
        for (int i = 0; i < passes.Count; i++)
        {
            IWorldGenerationPass generationPass = passes[i];
            if (generationPass == null) continue;
            generationPass.Execute(context);
        }

        return context.Result;
    }
}
