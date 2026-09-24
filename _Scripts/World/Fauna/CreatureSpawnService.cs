using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CreatureSpawnService : MonoBehaviour
{
    public static CreatureSpawnService Instance { get; private set; }

    [SerializeField] private BiomeCatalogSO biomeCatalog;
    [SerializeField] private Transform creatureParent;
    [SerializeField] private bool generateOnNewWorld = true;

    private GridManager subscribedGrid;
    private Coroutine spawnRoutine;
    private readonly List<Vector2Int> candidateReservoir =
        new List<Vector2Int>(64);
    private readonly List<Vector2Int> selectedPositions =
        new List<Vector2Int>(16);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    private void Start()
    {
        subscribedGrid = GridManager.Instance;
        if (subscribedGrid == null)
        {
            Debug.LogError("[CreatureSpawnService] GridManager não encontrado.", this);
            return;
        }

        subscribedGrid.OnWorldGenerated += HandleWorldGenerated;
        if (generateOnNewWorld && subscribedGrid.LastGenerationResult != null)
            HandleWorldGenerated(subscribedGrid.LastGenerationResult);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (subscribedGrid != null)
            subscribedGrid.OnWorldGenerated -= HandleWorldGenerated;
    }

    private void HandleWorldGenerated(WorldGenerationResult result)
    {
        if (!generateOnNewWorld || result == null || SaveGameRuntime.IsLoading) return;
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        spawnRoutine = StartCoroutine(SpawnAfterNavigationReady(result));
    }

    private IEnumerator SpawnAfterNavigationReady(WorldGenerationResult result)
    {
        while (NavGraphGenerator.Instance == null
            || !NavGraphGenerator.Instance.IsGraphReady)
        {
            yield return null;
        }

        GenerateCreatures(result);
        spawnRoutine = null;
    }

    private void GenerateCreatures(WorldGenerationResult result)
    {
        if (biomeCatalog == null)
        {
            Debug.LogError("[CreatureSpawnService] BiomeCatalogSO não configurado.", this);
            return;
        }

        HashSet<BiomeDefinitionSO> processed = new HashSet<BiomeDefinitionSO>();
        TryGenerateBiome(biomeCatalog.fallbackBiome, result, processed);
        if (biomeCatalog.biomes == null) return;
        foreach (BiomeDefinitionSO biome in biomeCatalog.biomes)
            TryGenerateBiome(biome, result, processed);
    }

    private void TryGenerateBiome(
        BiomeDefinitionSO biome,
        WorldGenerationResult result,
        HashSet<BiomeDefinitionSO> processed)
    {
        if (biome == null || !processed.Add(biome)
            || biome.creatures == null) return;

        foreach (BiomeCreatureRule rule in biome.creatures)
        {
            if (rule == null || rule.definition == null
                || rule.definition.prefab == null) continue;
            GenerateRule(biome, rule, result);
        }
    }

    private void GenerateRule(
        BiomeDefinitionSO biome,
        BiomeCreatureRule rule,
        WorldGenerationResult result)
    {
        int maximum = Mathf.Max(rule.minimumInstances, rule.maximumInstances);
        if (maximum <= 0) return;

        int seed = StableHash(result.Seed, biome.biomeId,
            rule.definition.creatureId);
        System.Random random = new System.Random(seed);
        candidateReservoir.Clear();
        selectedPositions.Clear();
        int acceptedCandidates = 0;
        int successfulSpawnRolls = 0;
        int reservoirCapacity = Mathf.Max(maximum, maximum * 8);
        int[] surfaceHeights = BuildSurfaceHeightMap(result);

        for (int x = 0; x < result.Width; x++)
        {
            for (int y = 1; y < result.Height; y++)
            {
                if (!IsValidCandidate(
                    biome, rule, result, surfaceHeights, x, y)) continue;
                acceptedCandidates++;
                if (random.NextDouble() <= rule.spawnChancePerCandidate)
                    successfulSpawnRolls++;
                Vector2Int candidate = new Vector2Int(x, y);
                if (candidateReservoir.Count < reservoirCapacity)
                    candidateReservoir.Add(candidate);
                else
                {
                    int replacement = random.Next(acceptedCandidates);
                    if (replacement < reservoirCapacity)
                        candidateReservoir[replacement] = candidate;
                }
            }
        }

        Shuffle(candidateReservoir, random);
        int targetCount = Mathf.Clamp(
            successfulSpawnRolls,
            Mathf.Min(rule.minimumInstances, candidateReservoir.Count),
            Mathf.Min(maximum, candidateReservoir.Count));
        for (int i = 0; i < candidateReservoir.Count
            && selectedPositions.Count < targetCount; i++)
        {
            Vector2Int candidate = candidateReservoir[i];
            if (!IsSeparated(candidate, selectedPositions, rule.minimumSeparation))
                continue;
            selectedPositions.Add(candidate);
        }

        for (int i = 0; i < selectedPositions.Count; i++)
            SpawnCreature(rule.definition, selectedPositions[i], biome.biomeId);
    }

    private bool IsValidCandidate(
        BiomeDefinitionSO biome,
        BiomeCreatureRule rule,
        WorldGenerationResult result,
        int[] surfaceHeights,
        int x,
        int y)
    {
        if (result.GetBiomeId(x, y - 1) != biome.biomeId) return false;
        int depth = Mathf.Max(0, surfaceHeights[x] - (y - 1));
        if (!rule.SupportsDepth(depth)) return false;
        TileType ground = result.GetCell(x, y - 1).TerrainType;
        if (!rule.SupportsGround(ground)) return false;
        if (!NavGraphGenerator.Instance.IsNavigablePosition(x, y)) return false;
        if (Vector2Int.Distance(new Vector2Int(x, y), result.SpawnPosition)
            < rule.minimumDistanceFromWorldSpawn) return false;
        return true;
    }

    private CreatureController SpawnCreature(
        CreatureDefinitionSO definition,
        Vector2Int position,
        string biomeId)
    {
        Vector3 world = GridManager.Instance.GridToWorldPosition(position);
        GameObject instance = Instantiate(
            definition.prefab,
            world,
            Quaternion.identity,
            creatureParent);
        CreatureController controller = instance.GetComponent<CreatureController>();
        if (controller == null)
        {
            Debug.LogError("[CreatureSpawnService] Prefab sem CreatureController.", instance);
            Destroy(instance);
            return null;
        }
        controller.InitializeSpawn(definition, position, "Bioma", biomeId);
        return controller;
    }

    public CreatureController SpawnRestored(
        string creatureId,
        Vector2Int position,
        Vector2Int homePosition,
        string origin,
        string biomeId)
    {
        CreatureDefinitionSO definition = FindDefinition(creatureId);
        if (definition == null || definition.prefab == null)
        {
            Debug.LogWarning(
                "[CreatureSpawnService] Definition não encontrada no load: "
                + creatureId,
                this);
            return null;
        }

        Vector3 world = GridManager.Instance.GridToWorldPosition(position);
        GameObject instance = Instantiate(
            definition.prefab,
            world,
            Quaternion.identity,
            creatureParent);
        CreatureController controller = instance.GetComponent<CreatureController>();
        if (controller == null)
        {
            Destroy(instance);
            return null;
        }

        controller.InitializeRestored(
            definition,
            position,
            homePosition,
            origin,
            biomeId);
        return controller;
    }

    private CreatureDefinitionSO FindDefinition(string creatureId)
    {
        if (biomeCatalog == null || string.IsNullOrWhiteSpace(creatureId))
            return null;

        CreatureDefinitionSO found = FindDefinitionInBiome(
            biomeCatalog.fallbackBiome, creatureId);
        if (found != null || biomeCatalog.biomes == null) return found;

        for (int i = 0; i < biomeCatalog.biomes.Count; i++)
        {
            found = FindDefinitionInBiome(biomeCatalog.biomes[i], creatureId);
            if (found != null) return found;
        }
        return null;
    }

    private static CreatureDefinitionSO FindDefinitionInBiome(
        BiomeDefinitionSO biome,
        string creatureId)
    {
        if (biome == null || biome.creatures == null) return null;
        for (int i = 0; i < biome.creatures.Count; i++)
        {
            BiomeCreatureRule rule = biome.creatures[i];
            if (rule != null && rule.definition != null
                && rule.definition.creatureId == creatureId)
            {
                return rule.definition;
            }
        }
        return null;
    }

    private static int StableHash(float seed, string biomeId, string creatureId)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + seed.GetHashCode();
            hash = hash * 31 + StableStringHash(biomeId);
            hash = hash * 31 + StableStringHash(creatureId);
            return hash;
        }
    }

    private static int StableStringHash(string value)
    {
        unchecked
        {
            int hash = 23;
            if (string.IsNullOrEmpty(value)) return hash;
            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];
            return hash;
        }
    }

    private static int[] BuildSurfaceHeightMap(WorldGenerationResult result)
    {
        int[] heights = new int[result.Width];
        for (int x = 0; x < result.Width; x++)
        {
            heights[x] = 0;
            for (int y = result.Height - 1; y >= 0; y--)
            {
                if (result.GetCell(x, y).TerrainType == TileType.Empty) continue;
                heights[x] = y;
                break;
            }
        }
        return heights;
    }

    private static bool IsSeparated(
        Vector2Int candidate,
        List<Vector2Int> selected,
        int minimumSeparation)
    {
        int squaredMinimum = minimumSeparation * minimumSeparation;
        for (int i = 0; i < selected.Count; i++)
        {
            Vector2Int delta = selected[i] - candidate;
            if (delta.sqrMagnitude < squaredMinimum) return false;
        }
        return true;
    }

    private static void Shuffle(List<Vector2Int> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            Vector2Int temporary = values[i];
            values[i] = values[other];
            values[other] = temporary;
        }
    }
}
