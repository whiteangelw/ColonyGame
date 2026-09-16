using System.Collections.Generic;
using UnityEngine;

public class FloraManager : MonoBehaviour
{
    public static FloraManager Instance { get; private set; }

    [SerializeField] private List<FloraDefinitionSO> definitions =
        new List<FloraDefinitionSO>();
    [SerializeField] private FloraDefinitionSO devModeDefinition;

    private readonly List<FloraEntity> spawnedFlora =
        new List<FloraEntity>();
    private readonly HashSet<Vector2Int> occupiedPositions =
        new HashSet<Vector2Int>();
    private GridManager subscribedGrid;
    private WorldGenerationResult appliedGeneration;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void OnDestroy()
    {
        UnsubscribeFromGrid();
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        SubscribeToGrid();
        TryApplyPendingGeneration();
    }

    public FloraEntity SpawnDevFlora(Vector2Int position)
    {
        return SpawnFlora(devModeDefinition, position);
    }

    public FloraEntity SpawnFlora(
        FloraDefinitionSO definition,
        Vector2Int position,
        int portions = -1)
    {
        if (definition == null || definition.prefab == null
            || GridManager.Instance == null
            || !GridManager.Instance.IsStandable(position)
            || occupiedPositions.Contains(position))
        {
            return null;
        }

        GameObject instance = Instantiate(
            definition.prefab,
            GridManager.Instance.GridToWorldPosition(position),
            Quaternion.identity);
        FloraEntity source = instance.GetComponent<FloraEntity>();

        if (source == null)
        {
            source = definition.providesFood
                ? instance.AddComponent<HarvestableFoodSource>()
                : instance.AddComponent<FloraEntity>();
        }

        source.Initialize(definition, portions);
        spawnedFlora.Add(source);
        occupiedPositions.Add(position);
        return source;
    }

    public FloraEntity SpawnFromSave(
        string floraId,
        Vector2Int position,
        int portions)
    {
        return SpawnFlora(FindDefinition(floraId), position, portions);
    }

    public List<FloraEntity> GetSnapshot()
    {
        spawnedFlora.RemoveAll(source => source == null);
        return new List<FloraEntity>(spawnedFlora);
    }

    public void Unregister(FloraEntity source)
    {
        if (source == null) return;
        spawnedFlora.Remove(source);
        occupiedPositions.Remove(source.GridPosition);
    }

    public void ClearForLoad()
    {
        foreach (FloraEntity source in GetSnapshot())
        {
            if (source != null) Destroy(source.gameObject);
        }

        spawnedFlora.Clear();
        occupiedPositions.Clear();
        appliedGeneration = null;
    }

    private void SubscribeToGrid()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || subscribedGrid == grid) return;

        UnsubscribeFromGrid();
        subscribedGrid = grid;
        subscribedGrid.OnWorldGenerated += HandleWorldGenerated;
    }

    private void UnsubscribeFromGrid()
    {
        if (subscribedGrid == null) return;
        subscribedGrid.OnWorldGenerated -= HandleWorldGenerated;
        subscribedGrid = null;
    }

    private void TryApplyPendingGeneration()
    {
        if (subscribedGrid != null && subscribedGrid.IsGridReady)
        {
            HandleWorldGenerated(subscribedGrid.LastGenerationResult);
        }
    }

    private void HandleWorldGenerated(WorldGenerationResult result)
    {
        if (result == null || result == appliedGeneration
            || SaveGameRuntime.IsLoading)
        {
            return;
        }

        appliedGeneration = result;
        IReadOnlyList<GeneratedFloraData> generated = result.GeneratedFlora;
        for (int i = 0; i < generated.Count; i++)
        {
            GeneratedFloraData entry = generated[i];
            FloraDefinitionSO definition = entry.Definition != null
                ? entry.Definition
                : FindDefinition(entry.FloraId);
            SpawnFlora(definition, entry.Position, entry.InitialPortions);
        }
    }

    private FloraDefinitionSO FindDefinition(string floraId)
    {
        return definitions.Find(definition => definition != null
            && definition.floraId == floraId);
    }
}
