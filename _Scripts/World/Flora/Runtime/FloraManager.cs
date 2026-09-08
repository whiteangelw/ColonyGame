using System.Collections.Generic;
using UnityEngine;

public class FloraManager : MonoBehaviour
{
    public static FloraManager Instance { get; private set; }

    [SerializeField] private List<FloraDefinitionSO> definitions =
        new List<FloraDefinitionSO>();
    [SerializeField] private FloraDefinitionSO devModeDefinition;

    private readonly List<HarvestableFoodSource> spawnedFlora =
        new List<HarvestableFoodSource>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public HarvestableFoodSource SpawnDevFlora(Vector2Int position)
    {
        return SpawnFlora(devModeDefinition, position);
    }

    public HarvestableFoodSource SpawnFlora(
        FloraDefinitionSO definition,
        Vector2Int position,
        int portions = -1)
    {
        if (definition == null || definition.prefab == null
            || GridManager.Instance == null
            || !GridManager.Instance.IsStandable(position))
        {
            return null;
        }

        GameObject instance = Instantiate(
            definition.prefab,
            GridManager.Instance.GridToWorldPosition(position),
            Quaternion.identity);
        HarvestableFoodSource source =
            instance.GetComponent<HarvestableFoodSource>();

        if (source == null)
        {
            source = instance.AddComponent<HarvestableFoodSource>();
        }

        source.Initialize(definition, portions);
        spawnedFlora.Add(source);
        return source;
    }

    public HarvestableFoodSource SpawnFromSave(
        string floraId,
        Vector2Int position,
        int portions)
    {
        return SpawnFlora(FindDefinition(floraId), position, portions);
    }

    public List<HarvestableFoodSource> GetSnapshot()
    {
        spawnedFlora.RemoveAll(source => source == null);
        return new List<HarvestableFoodSource>(spawnedFlora);
    }

    public void ClearForLoad()
    {
        foreach (HarvestableFoodSource source in GetSnapshot())
        {
            if (source != null) Destroy(source.gameObject);
        }

        spawnedFlora.Clear();
    }

    private FloraDefinitionSO FindDefinition(string floraId)
    {
        return definitions.Find(definition => definition != null
            && definition.floraId == floraId);
    }
}
