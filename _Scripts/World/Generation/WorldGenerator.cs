using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class WorldGenerator : MonoBehaviour
{
    private static readonly ProfilerMarker GenerateMarker =
        new ProfilerMarker("WorldGeneration.Generate");
    [System.Serializable]
    public class BiomeLayer
    {
        public BiomeData biome;
        public int startDepth = 0;
    }

    [Header("Referências")]
    [SerializeField] private GridManager gridManager;

    [Header("Preset do Mundo (Opcional)")]
    [SerializeField] private WorldPreset activePreset; // Adicionado aqui!

    [Header("Semente do Mundo")]
    [SerializeField] private bool randomSeed = true;
    [SerializeField] private float seed;

    [Header("Relevo Global (Perlin 1D)")]
    [SerializeField] private float surfaceFrequency = 0.025f;
    [SerializeField] private int surfaceHeight = 50;
    [SerializeField] private int heightVariation = 8;

    [Header("Camadas de Biomas (Usadas se não houver Preset)")]
    [SerializeField] private List<BiomeLayer> biomeLayers = new List<BiomeLayer>();

    [Header("Lagos da Superficie")]
    [SerializeField] private int numberOfLakes = 3;
    [SerializeField] private int lakeWidth = 6;
    [SerializeField] private int lakeDepth = 3;

    private Vector2Int spawnCenter;

    public float CurrentSeed => seed;

    private void Awake()
    {
        if (gridManager == null)
            gridManager = GetComponent<GridManager>();

        if (randomSeed)
            seed = Random.Range(0f, 10000f);

        // Se houver um Preset configurado, substitui a lista local
        if (activePreset != null && activePreset.biomeLayers.Count > 0)
        {
            biomeLayers = activePreset.biomeLayers;
        }
    }

    public void GenerateWorld()
    {
        if (gridManager == null || biomeLayers.Count == 0) return;

        long startedAt = PerformanceMetricsService.BeginSample();
        using (GenerateMarker.Auto())
        {

        int width = gridManager.width;
        int height = gridManager.height;

        // 1. GERAR TERRENO E APLICAR BIOMAS POR PROFUNDIDADE
        for (int x = 0; x < width; x++)
        {
            float noiseVal = Mathf.PerlinNoise((x + seed) * surfaceFrequency, seed);
            int currentSurface = surfaceHeight + Mathf.FloorToInt(noiseVal * heightVariation);

            for (int y = 0; y < height; y++)
            {
                TileType selectedType = DetermineTileType(x, y, currentSurface);
                gridManager.SetTileType(x, y, selectedType);

                Tile tile = gridManager.GetTile(x, y);
                if (tile != null)
                {
                    tile.fogState = (y >= currentSurface) ? FogState.Revealed : FogState.Unexplored;
                }
            }
        }

        // 2. LAGOAS NA SUPERFÍCIE
        GenerateSurfaceLakes(width, height);

        // 3. ZONA DE SPAWN SEGUIRA O RELEVO
        SetupNaturalSpawnArea(width, height);

        // 4. APLICA CAMADA DE COPERTURA (Grama ou Superfície do Bioma)
        ApplySurfaceLayer(width, height);
        }
        PerformanceMetricsService.EndSample(
            PerformanceMetric.WorldGeneration,
            startedAt);
    }

    private BiomeData GetBiomeAtDepth(int depth)
    {
        BiomeData activeBiome = biomeLayers[0].biome;

        for (int i = 0; i < biomeLayers.Count; i++)
        {
            if (depth >= biomeLayers[i].startDepth)
            {
                activeBiome = biomeLayers[i].biome;
            }
        }

        return activeBiome;
    }

    private TileType DetermineTileType(int x, int y, int currentSurfaceHeight)
    {
        if (y > currentSurfaceHeight) return TileType.Empty;

        int depth = currentSurfaceHeight - y;
        BiomeData biome = GetBiomeAtDepth(depth);

        if (biome == null) return TileType.Solid;

        // 1. Cavernas do Bioma
        float caveNoise = Mathf.PerlinNoise((x + seed) * biome.caveScale, (y + seed) * biome.caveScale);
        if (caveNoise < biome.caveThreshold && depth > 2)
        {
            return TileType.Empty;
        }

        // 2. Minérios do Bioma
        foreach (var ore in biome.ores)
        {
            if (depth >= ore.minDepth && depth <= ore.maxDepth)
            {
                float oreOffset = (int)ore.tileType * 1000f;
                float oreNoise = Mathf.PerlinNoise((x + seed + oreOffset) * ore.frequency, (y + seed + oreOffset) * ore.frequency);

                if (oreNoise > ore.threshold)
                {
                    return ore.tileType;
                }
            }
        }

        // 3. Preenchimento padrão do Bioma
        return biome.fillTile;
    }

    private void ApplySurfaceLayer(int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = height - 1; y >= 0; y--)
            {
                Tile current = gridManager.GetTile(x, y);
                if (current != null && current.type != TileType.Empty)
                {
                    Tile above = gridManager.GetTile(x, y + 1);
                    if ((above == null || above.type == TileType.Empty) && (above == null || above.liquidAmount <= 0.01f))
                    {
                        int depth = surfaceHeight - y;
                        BiomeData biome = GetBiomeAtDepth(depth);

                        if (biome != null && current.type == biome.fillTile)
                        {
                            gridManager.SetTileType(x, y, biome.surfaceTile);
                        }
                    }
                    break;
                }
            }
        }
    }

    private void SetupNaturalSpawnArea(int width, int height)
    {
        int cx = width / 2;
        int surfaceY = 0;

        for (int y = height - 1; y >= 0; y--)
        {
            if (gridManager.GetTileType(cx, y) != TileType.Empty)
            {
                surfaceY = y;
                break;
            }
        }

        spawnCenter = new Vector2Int(cx, surfaceY + 1);

        for (int x = cx - 2; x <= cx + 2; x++)
        {
            for (int y = surfaceY + 1; y <= surfaceY + 4; y++)
            {
                gridManager.SetTileType(x, y, TileType.Empty);

                Tile t = gridManager.GetTile(x, y);
                if (t != null)
                {
                    t.fogState = FogState.Revealed;
                    gridManager.SetLiquidAmount(x, y, 0f);
                }
            }

            Tile groundTile = gridManager.GetTile(x, surfaceY);
            if (groundTile != null)
            {
                gridManager.SetLiquidAmount(x, surfaceY, 0f);
                groundTile.fogState = FogState.Revealed;
            }
        }
    }

    private void GenerateSurfaceLakes(int width, int height)
    {
        if (LiquidManager.Instance == null || numberOfLakes <= 0) return;

        int minX = 5;
        int maxX = width - 5;
        int step = (maxX - minX) / (numberOfLakes + 1);

        for (int i = 1; i <= numberOfLakes; i++)
        {
            int lakeCenterX = minX + (step * i) + Random.Range(-2, 3);
            if (Mathf.Abs(lakeCenterX - (width / 2)) < 8) continue;

            int topY = 0;
            for (int y = height - 1; y >= 0; y--)
            {
                if (gridManager.GetTileType(lakeCenterX, y) != TileType.Empty)
                {
                    topY = y;
                    break;
                }
            }

            int halfW = lakeWidth / 2;
            for (int x = lakeCenterX - halfW; x <= lakeCenterX + halfW; x++)
            {
                if (x <= 1 || x >= width - 2) continue;

                int distFromCenter = Mathf.Abs(x - lakeCenterX);
                int depthAtX = lakeDepth - Mathf.FloorToInt((float)distFromCenter / halfW * lakeDepth);

                for (int d = 0; d < depthAtX; d++)
                {
                    int targetY = topY - d;
                    gridManager.SetTileType(x, targetY, TileType.Empty);
                    LiquidManager.Instance.AddLiquid(x, targetY, 0.95f);
                }

                int bottomY = topY - depthAtX;
                if (bottomY >= 0 && gridManager.GetTileType(x, bottomY) == TileType.Empty)
                {
                    gridManager.SetTileType(x, bottomY, TileType.Solid);
                }
            }
        }
    }

    public Vector2Int GetSpawnPosition()
    {
        return spawnCenter;
    }
}
