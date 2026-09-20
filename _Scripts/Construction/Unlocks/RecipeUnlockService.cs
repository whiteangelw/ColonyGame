using System;
using System.Collections.Generic;
using UnityEngine;

public class RecipeUnlockService : MonoBehaviour
{
    public static RecipeUnlockService Instance { get; private set; }

    [Tooltip("Mantém todas as definições liberadas. Desative para testar progressão.")]
    [SerializeField] private bool startWithAllRecipesUnlocked = true;

    [Header("Definições liberadas no início")]
    [Tooltip("Arraste os BuildDefinitionSO que devem existir desde o começo. Alterações aqui também valem para saves antigos.")]
    [SerializeField] private List<BuildDefinitionSO> initiallyUnlockedDefinitions =
        new List<BuildDefinitionSO>();

    [Header("Compatibilidade legada — não preencher em conteúdo novo")]
    [Tooltip("IDs antigos preservados para não quebrar cenas/prefabs já configurados.")]
    [SerializeField] private List<string> initiallyUnlockedDefinitionIds =
        new List<string>();
    [Tooltip("Usado somente por cartas e saves antigos baseados em TileType.")]
    [SerializeField] private List<TileType> initiallyUnlockedRecipes =
        new List<TileType>();

    // Estes conjuntos guardam apenas progresso persistente. Os padrões do
    // Inspector são consultados separadamente e, portanto, continuam valendo
    // mesmo quando um save antigo é carregado.
    private readonly HashSet<string> unlockedDefinitionIds =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<TileType> unlockedLegacyRecipes =
        new HashSet<TileType>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsUnlocked(BuildDefinitionSO definition)
    {
        if (definition == null) return false;

        return startWithAllRecipesUnlocked
            || IsConfiguredAsInitiallyUnlocked(definition)
            || IsDefinitionIdUnlocked(definition.DefinitionId)
            || IsLegacyTileUnlocked(definition.TileType);
    }

    public bool IsUnlocked(string definitionId)
    {
        if (startWithAllRecipesUnlocked) return true;

        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(definitionId);

        return definition != null
            ? IsUnlocked(definition)
            : IsConfiguredId(definitionId) || IsDefinitionIdUnlocked(definitionId);
    }

    // Mantido somente para conteúdo e saves antigos.
    public bool IsUnlocked(TileType tileType)
    {
        return startWithAllRecipesUnlocked || IsLegacyTileUnlocked(tileType);
    }

    public bool Unlock(BuildDefinitionSO definition)
    {
        if (definition == null) return false;
        return Unlock(definition.DefinitionId);
    }

    public bool Unlock(string definitionId)
    {
        string normalizedId = NormalizeId(definitionId);
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(normalizedId);

        if (string.IsNullOrEmpty(normalizedId)
            || definition == null
            || IsUnlocked(definition))
        {
            return false;
        }

        unlockedDefinitionIds.Add(normalizedId);
        GameEvents.TriggerBuildDefinitionUnlocked(normalizedId);
        GameEvents.TriggerRecipeUnlockStateChanged();
        return true;
    }

    public bool Unlock(TileType tileType)
    {
        if (IsUnlocked(tileType)) return false;

        unlockedLegacyRecipes.Add(tileType);
        GameEvents.TriggerRecipeUnlocked(tileType);
        GameEvents.TriggerRecipeUnlockStateChanged();
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
            result.Add((int)tileType);

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
                AddDefinitionIdWithoutNotification(definitionId);
        }

        if (legacyTileTypes != null)
        {
            foreach (int value in legacyTileTypes)
            {
                if (Enum.IsDefined(typeof(TileType), value))
                    unlockedLegacyRecipes.Add((TileType)value);
            }
        }

        GameEvents.TriggerRecipeUnlockStateChanged();
    }

    public void ResetToConfiguredDefaults(bool notify = true)
    {
        // Os padrões não precisam ser copiados para o save. Limpar os conjuntos
        // significa voltar exatamente ao estado configurado no Inspector.
        unlockedDefinitionIds.Clear();
        unlockedLegacyRecipes.Clear();
        if (notify) GameEvents.TriggerRecipeUnlockStateChanged();
    }

    private bool IsConfiguredAsInitiallyUnlocked(BuildDefinitionSO definition)
    {
        for (int i = 0; i < initiallyUnlockedDefinitions.Count; i++)
        {
            if (initiallyUnlockedDefinitions[i] == definition) return true;
        }

        return IsConfiguredId(definition.DefinitionId);
    }

    private bool IsConfiguredId(string definitionId)
    {
        string normalizedId = NormalizeId(definitionId);
        if (string.IsNullOrEmpty(normalizedId)) return false;

        for (int i = 0; i < initiallyUnlockedDefinitionIds.Count; i++)
        {
            if (string.Equals(
                NormalizeId(initiallyUnlockedDefinitionIds[i]),
                normalizedId,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLegacyTileUnlocked(TileType tileType)
    {
        return unlockedLegacyRecipes.Contains(tileType)
            || initiallyUnlockedRecipes.Contains(tileType);
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
            unlockedDefinitionIds.Add(normalizedId);
    }

    private static string NormalizeId(string definitionId)
    {
        return definitionId?.Trim() ?? string.Empty;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        HashSet<BuildDefinitionSO> seen = new HashSet<BuildDefinitionSO>();
        for (int i = initiallyUnlockedDefinitions.Count - 1; i >= 0; i--)
        {
            BuildDefinitionSO definition = initiallyUnlockedDefinitions[i];
            if (definition == null || !seen.Add(definition))
                initiallyUnlockedDefinitions.RemoveAt(i);
        }
    }
#endif
}
