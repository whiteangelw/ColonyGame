using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Mantém a configuração de geração e produz dados de mundo.
/// Não escreve no GridManager nem aciona sistemas de runtime/apresentação.
/// </summary>
public class WorldGenerator : MonoBehaviour
{
    private static readonly ProfilerMarker GenerateMarker =
        new ProfilerMarker("WorldGeneration.Generate");

    [Header("Preset do Mundo (Opcional)")]
    [SerializeField] private WorldPreset activePreset;

    [Header("Semente do Mundo")]
    [SerializeField] private bool randomSeed = true;
    [SerializeField] private float seed;

    [Header("Compatibilidade da Geração")]
    [SerializeField, Min(1)] private int generationVersion = 1;

    [Header("Relevo Global (Perlin 1D)")]
    [SerializeField] private float surfaceFrequency = 0.025f;
    [SerializeField] private int surfaceHeight = 50;
    [SerializeField] private int heightVariation = 8;

    [Header("P5D.1 - Escala Vertical (Generation Version 4)")]
    [SerializeField, Range(0.10f, 0.90f)]
    private float relativeSurfaceHeight = 0.70f;
    [SerializeField, Min(2)] private int minimumSkyHeight = 20;
    [SerializeField, Min(2)] private int minimumTerrainDepth = 30;

    [Header("Camadas de Biomas (Usadas se não houver Preset)")]
    [SerializeField] private List<WorldBiomeLayer> biomeLayers =
        new List<WorldBiomeLayer>();

    [Header("P5C/P5D - Catálogo de Biomas (Generation Version 2+)")]
    [SerializeField] private BiomeCatalogSO biomeCatalog;

    [Header("Lagos da Superficie")]
    [SerializeField] private int numberOfLakes = 3;
    [SerializeField] private int lakeWidth = 6;
    [SerializeField] private int lakeDepth = 3;

    private Vector2Int spawnPosition;

    public float CurrentSeed => seed;
    public int CurrentGenerationVersion => generationVersion;

    private void Awake()
    {
        if (randomSeed)
        {
            seed = WorldGenerationSeed.CreateUnpredictableSeed();
        }
    }

    public bool TryGenerateWorldData(
        int width,
        int height,
        out WorldGenerationResult result)
    {
        result = null;
        int effectiveVersion = Mathf.Max(1, generationVersion);
        IReadOnlyList<WorldBiomeLayer> layers = ResolveBiomeLayers();

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (effectiveVersion == 1 && layers.Count == 0)
        {
            Debug.LogError(
                "[WorldGenerator] Generation Version 1 exige ao menos "
                + "uma WorldBiomeLayer configurada.",
                this);
            return false;
        }

        if (!WorldGenerationPassFactory.IsSupported(effectiveVersion))
        {
            Debug.LogError(
                $"[WorldGenerator] generationVersion {effectiveVersion} "
                + "não possui pipeline registrado.",
                this);
            return false;
        }

        if (effectiveVersion >= 2
            && (biomeCatalog == null || !biomeCatalog.IsConfigured))
        {
            Debug.LogError(
                "[WorldGenerator] Generation Version 2 exige um "
                + "BiomeCatalogSO configurado.",
                this);
            return false;
        }

        WorldGenerationSettings settings = new WorldGenerationSettings(
            width,
            height,
            seed,
            effectiveVersion,
            surfaceFrequency,
            surfaceHeight,
            heightVariation,
            relativeSurfaceHeight,
            minimumSkyHeight,
            minimumTerrainDepth,
            layers,
            biomeCatalog,
            numberOfLakes,
            lakeWidth,
            lakeDepth);

        long startedAt = PerformanceMetricsService.BeginSample();
        using (GenerateMarker.Auto())
        {
            result = LegacyWorldGeneration.Generate(settings);
            spawnPosition = result.SpawnPosition;
        }

        PerformanceMetricsService.EndSample(
            PerformanceMetric.WorldGeneration,
            startedAt);

        return result != null;
    }

    public Vector2Int GetSpawnPosition()
    {
        return spawnPosition;
    }

    private IReadOnlyList<WorldBiomeLayer> ResolveBiomeLayers()
    {
        if (activePreset != null
            && activePreset.biomeLayers != null
            && activePreset.biomeLayers.Count > 0)
        {
            return activePreset.biomeLayers;
        }

        return biomeLayers;
    }
}
