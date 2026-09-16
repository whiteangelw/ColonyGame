using System;

/// <summary>
/// Configura a partir de qual profundidade um bioma passa a ser usado.
/// </summary>
[Serializable]
public sealed class WorldBiomeLayer
{
    public BiomeData biome;
    public int startDepth;
}
