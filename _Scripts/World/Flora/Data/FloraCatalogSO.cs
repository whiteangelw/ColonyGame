using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "FloraCatalog",
    menuName = "Colony/Flora/Catalog")]
public sealed class FloraCatalogSO : ScriptableObject
{
    [SerializeField] private List<FloraDefinitionSO> definitions =
        new List<FloraDefinitionSO>();

    private Dictionary<string, FloraDefinitionSO> definitionsById;

    public IReadOnlyList<FloraDefinitionSO> Definitions => definitions;

    public bool TryGetDefinition(
        string floraId,
        out FloraDefinitionSO definition)
    {
        EnsureLookupIsReady();

        if (string.IsNullOrWhiteSpace(floraId))
        {
            definition = null;
            return false;
        }

        return definitionsById.TryGetValue(floraId, out definition);
    }

    public FloraDefinitionSO GetDefinition(string floraId)
    {
        TryGetDefinition(floraId, out FloraDefinitionSO definition);
        return definition;
    }

    private void OnEnable()
    {
        RebuildLookup(logProblems: false);
    }

    private void OnValidate()
    {
        RebuildLookup(logProblems: true);
    }

    private void EnsureLookupIsReady()
    {
        if (definitionsById == null)
        {
            RebuildLookup(logProblems: false);
        }
    }

    private void RebuildLookup(bool logProblems)
    {
        definitionsById = new Dictionary<string, FloraDefinitionSO>(
            System.StringComparer.Ordinal);

        if (definitions == null) return;

        for (int index = 0; index < definitions.Count; index++)
        {
            FloraDefinitionSO definition = definitions[index];
            if (definition == null)
            {
                LogProblem(
                    logProblems,
                    $"A posição {index} do catálogo está vazia.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.floraId))
            {
                LogProblem(
                    logProblems,
                    $"A definição '{definition.name}' não possui Flora Id.",
                    definition);
                continue;
            }

            if (definition.prefab == null)
            {
                LogProblem(
                    logProblems,
                    $"A flora '{definition.floraId}' não possui prefab.",
                    definition);
            }

            if (!definitionsById.TryAdd(definition.floraId, definition))
            {
                LogProblem(
                    logProblems,
                    $"O Flora Id '{definition.floraId}' está duplicado.",
                    definition);
            }
        }
    }

    private void LogProblem(
        bool shouldLog,
        string message,
        Object context = null)
    {
        if (!shouldLog) return;
        Debug.LogWarning($"[FloraCatalog] {message}", context ?? this);
    }
}
