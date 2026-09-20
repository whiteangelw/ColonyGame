using System;
using System.Runtime.InteropServices;

/// <summary>
/// RNG pequeno e estável entre versões do runtime. Cada pass recebe sua
/// própria instância, impedindo que mudanças em um pass alterem os demais.
/// </summary>
public struct DeterministicRandom
{
    private uint state;

    public DeterministicRandom(int seed)
    {
        state = unchecked((uint)seed);
        if (state == 0u) state = 0x6D2B79F5u;
    }

    public int NextInt(int minimumInclusive, int maximumExclusive)
    {
        if (maximumExclusive <= minimumInclusive)
        {
            return minimumInclusive;
        }

        uint range = (uint)(maximumExclusive - minimumInclusive);
        return minimumInclusive + (int)(NextUInt() % range);
    }

    public float NextFloat01()
    {
        return (NextUInt() >> 8) * (1f / 16777216f);
    }

    private uint NextUInt()
    {
        uint value = state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        state = value;
        return value;
    }
}

public static class WorldGenerationSeed
{
    public static int Derive(float worldSeed, int generationVersion, string passId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = Mix(hash, FloatBits.ToInt32(worldSeed));
            hash = Mix(hash, generationVersion);

            if (!string.IsNullOrEmpty(passId))
            {
                for (int i = 0; i < passId.Length; i++)
                {
                    hash ^= passId[i];
                    hash *= 16777619u;
                }
            }

            return (int)hash;
        }
    }

    public static float CreateUnpredictableSeed()
    {
        unchecked
        {
            int value = Guid.NewGuid().GetHashCode() & int.MaxValue;
            return (value % 10000000) / 1000f;
        }
    }

    public static float Sample01(int passSeed, int x, int y, int salt = 0)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = Mix(hash, passSeed);
            hash = Mix(hash, x);
            hash = Mix(hash, y);
            hash = Mix(hash, salt);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (hash >> 8) * (1f / 16777216f);
        }
    }

    private static uint Mix(uint hash, int value)
    {
        unchecked
        {
            hash ^= (byte)value;
            hash *= 16777619u;
            hash ^= (byte)(value >> 8);
            hash *= 16777619u;
            hash ^= (byte)(value >> 16);
            hash *= 16777619u;
            hash ^= (byte)(value >> 24);
            hash *= 16777619u;
            return hash;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] private float floatValue;
        [FieldOffset(0)] private int intValue;

        public static int ToInt32(float value)
        {
            FloatBits bits = new FloatBits { floatValue = value };
            return bits.intValue;
        }
    }
}
