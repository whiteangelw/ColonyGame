using System;
using System.Collections.Generic;
using UnityEngine;

public class RecipeUnlockService : MonoBehaviour
{
    public static RecipeUnlockService Instance { get; private set; }

    [Tooltip("Mantém todas as definições liberadas. Desative para testar progressão.")]
    [SerializeField] private bool startWithAllRecipesUnlocked = true;

    [Header("Definições liberadas no início")]
    [Tooltip("Use os Definition Ids dos BuildDefinitionSO.")]
    [SerializeField] private List<string> initiallyUnlockedDefinitionIds =
        new List<string>();

    [Header("Compatibilidade legada")]
    [Tooltip("Usado por cartas e saves antigos baseados em TileType.")]
    [SerializeField] private List<TileType> initiallyUnlockedRecipes =
        new List<TileType>();

    private readonly HashSet<string> unlockedDefinitionIds =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<TileType> unlockedLegacyRecipes =
        new HashSet<TileType>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        ResetToConfiguredDefaults(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsUnlocked(BuildDefinitionSO definition)
    {
        if (definition == null) return false;

        return startWithAllRecipesUnlocked
            || IsDefinitionIdUnlocked(definition.DefinitionId)
            || unlockedLegacyRecipes.Contains(definition.TileType);
    }

    public bool IsUnlocked(string definitionId)
    {
        if (startWithAllRecipesUnlocked) return true;

        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(definitionId);

        return definition != null
            ? IsUnlocked(definition)
            : IsDefinitionIdUnlocked(definitionId);
    }

    // Mantido para cartas e receitas antigas.
    public bool IsUnlocked(TileType tileType)
    {
        return startWithAllRecipesUnlocked
            || unlockedLegacyRecipes.Contains(tileType);
    }

    public bool Unlock(string definitionId)
    {
        string normalizedId = NormalizeId(definitionId);
        if (string.IsNullOrEmpty(normalizedId)
            || BuildCatalogService.Instance?.GetById(normalizedId) == null
            || IsUnlocked(normalizedId))
        {
            return false;
        }

        unlockedDefinitionIds.Add(normalizedId);
        GameEvents.TriggerBuildDefinitionUnlocked(normalizedId);
        return true;
    }

    // Compatibilidade com cartas antigas baseadas em TileType.
    public bool Unlock(TileType tileType)
    {
        if (IsUnlocked(tileType)) return false;

        unlockedLegacyRecipes.Add(tileType);
        GameEvents.TriggerRecipeUnlocked(tileType);
        return true;
    }

    public List<string> GetUnlockedDefinitionIdsSnapshot()
    {
        List<string> result = new List<string>(unlockedDefinitionIds);
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public List<int> GetUnlockedLegacyTileTypesSnapshot()
    {
        List<int> result = new List<int>();
        foreach (TileType tileType in unlockedLegacyRecipes)
        {
            result.Add((int)tileType);
        }

        result.Sort();
        return result;
    }

    public void RestoreState(
        IEnumerable<string> definitionIds,
        IEnumerable<int> legacyTileTypes)
    {
        unlockedDefinitionIds.Clear();
        unlockedLegacyRecipes.Clear();

        if (definitionIds != null)
        {
            foreach (string definitionId in definitionIds)
            {
                AddDefinitionIdWithoutNotification(definitionId);
            }
        }

        if (legacyTileTypes != null)
        {
            foreach (int value in legacyTileTypes)
            {
                if (Enum.IsDefined(typeof(TileType), value))
                {
                    unlockedLegacyRecipes.Add((TileType)value);
                }
            }
        }

        GameEvents.TriggerRecipeUnlockStateChanged();
    }

    public void ResetToConfiguredDefaults(bool notify = true)
    {
        unlockedDefinitionIds.Clear();
        unlockedLegacyRecipes.Clear();

        foreach (string definitionId in initiallyUnlockedDefinitionIds)
        {
            AddDefinitionIdWithoutNotification(definitionId);
        }

        foreach (TileType tileType in initiallyUnlockedRecipes)
        {
            unlockedLegacyRecipes.Add(tileType);
        }

        if (notify) GameEvents.TriggerRecipeUnlockStateChanged();
    }

    private bool IsDefinitionIdUnlocked(string definitionId)
    {
        string normalizedId = NormalizeId(definitionId);
        return !string.IsNullOrEmpty(normalizedId)
            && unlockedDefinitionIds.Contains(normalizedId);
    }

    private void AddDefinitionIdWithoutNotification(string definitionId)
    {
        string normalizedId = NormalizeId(definitionId);
        if (!string.IsNullOrEmpty(normalizedId))
        {
            unlockedDefinitionIds.Add(normalizedId);
        }
    }

    private static string NormalizeId(string definitionId)
    {
        return definitionId?.Trim() ?? string.Empty;
    }
}
