using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BuildMenuUI : MonoBehaviour
{
    [Header("Container Principal da UI")]
    [SerializeField] private GameObject menuPanelContainer;

    [Header("Configuração dos Slots")]
    [SerializeField] private GameObject itemSlotPrefab;
    [SerializeField] private Transform contentParent;
    [SerializeField] private BuildMenuTooltipUI tooltip;

    [Header("Categorias geradas pelo catálogo")]
    [SerializeField] private Transform categoryButtonParent;
    [SerializeField] private GameObject categoryButtonPrefab;

    [Header("Busca")]
    [SerializeField] private TMP_InputField searchInput;

    [Header("Fallback legado — não usar para conteúdo novo")]
    [Tooltip("Usado somente quando o BuildCatalogSO não estiver disponível.")]
    [SerializeField] private List<BuildRecipe> buildRecipes = new List<BuildRecipe>();

    private BuildCategory? currentFilter;
    private string searchTerm = string.Empty;

    private void Awake()
    {
        if (menuPanelContainer != null) menuPanelContainer.SetActive(false);
    }

    private void Start()
    {
        if (searchInput != null)
        {
            searchInput.onValueChanged.AddListener(SetSearchTerm);
        }

        RefreshMenu();
    }

    private void OnEnable()
    {
        GameEvents.OnToggleBuildMenuRequested += HandleToggleMenuRequested;
        GameEvents.OnResourceUnlocked += HandleResourceUnlocked;
        GameEvents.OnRecipeUnlocked += HandleRecipeUnlocked;
        GameEvents.OnBuildDefinitionUnlocked += HandleDefinitionUnlocked;
        GameEvents.OnRecipeUnlockStateChanged += RefreshMenu;
    }

    private void OnDisable()
    {
        GameEvents.OnToggleBuildMenuRequested -= HandleToggleMenuRequested;
        GameEvents.OnResourceUnlocked -= HandleResourceUnlocked;
        GameEvents.OnRecipeUnlocked -= HandleRecipeUnlocked;
        GameEvents.OnBuildDefinitionUnlocked -= HandleDefinitionUnlocked;
        GameEvents.OnRecipeUnlockStateChanged -= RefreshMenu;
        if (tooltip != null) tooltip.Hide();
    }

    private void OnDestroy()
    {
        if (searchInput != null) searchInput.onValueChanged.RemoveListener(SetSearchTerm);
    }

    private void HandleToggleMenuRequested(bool shouldOpen)
    {
        if (menuPanelContainer == null) return;
        menuPanelContainer.SetActive(shouldOpen);
        if (shouldOpen) RefreshMenu();
        else if (tooltip != null) tooltip.Hide();
    }

    private void HandleResourceUnlocked(ResourceType unused) { RefreshMenu(); }
    private void HandleRecipeUnlocked(TileType unused) { RefreshMenu(); }
    private void HandleDefinitionUnlocked(string unused) { RefreshMenu(); }

    public void SetFilter(BuildCategory? category)
    {
        currentFilter = category;
        RefreshMenu();
    }

    public void SetSearchTerm(string value)
    {
        searchTerm = value == null ? string.Empty : value.Trim();
        RefreshMenu();
    }

    public void RefreshMenu()
    {
        if (contentParent == null || itemSlotPrefab == null) return;

        if (tooltip != null) tooltip.Hide();
        ClearChildren(contentParent);

        IReadOnlyList<BuildDefinitionSO> definitions = BuildCatalogService.Instance?.Definitions;
        if (definitions != null && definitions.Count > 0)
        {
            List<BuildDefinitionSO> visibleDefinitions =
                CollectVisibleDefinitions(definitions);
            RebuildCategoryButtons(visibleDefinitions);
            CreateCatalogSlots(visibleDefinitions);
            return;
        }

        ClearChildren(categoryButtonParent);
        CreateLegacySlots();
    }

    private void RebuildCategoryButtons(IReadOnlyList<BuildDefinitionSO> definitions)
    {
        if (categoryButtonParent == null || categoryButtonPrefab == null) return;

        ClearChildren(categoryButtonParent);
        CreateCategoryButton("Todos", null);

        List<BuildCategory> categories = new List<BuildCategory>();
        foreach (BuildDefinitionSO definition in definitions)
        {
            if (definition != null && !categories.Contains(definition.Category))
            {
                categories.Add(definition.Category);
            }
        }

        categories.Sort((left, right) => string.Compare(
            left.ToString(), right.ToString(), StringComparison.Ordinal));

        foreach (BuildCategory category in categories)
        {
            CreateCategoryButton(category.ToString(), category);
        }
    }

    private void CreateCategoryButton(string label, BuildCategory? category)
    {
        GameObject buttonObject = Instantiate(categoryButtonPrefab, categoryButtonParent);
        BuildCategoryFilterButton button =
            buttonObject.GetComponent<BuildCategoryFilterButton>();
        if (button == null) button = buttonObject.AddComponent<BuildCategoryFilterButton>();

        button.Bind(label, category, currentFilter, SetFilter);
    }

    private void CreateCatalogSlots(IReadOnlyList<BuildDefinitionSO> definitions)
    {
        List<BuildDefinitionSO> ordered = new List<BuildDefinitionSO>();
        foreach (BuildDefinitionSO definition in definitions)
        {
            if (definition != null && MatchesCurrentView(definition)) ordered.Add(definition);
        }

        ordered.Sort(CompareDefinitions);
        foreach (BuildDefinitionSO definition in ordered) CreateDefinitionSlot(definition);
    }

    private void CreateDefinitionSlot(BuildDefinitionSO definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DefinitionId))
        {
            Debug.LogWarning("[BuildMenuUI] Definição sem ID ignorada: " + definition.name, definition);
            return;
        }

        GameObject slotObject = Instantiate(itemSlotPrefab, contentParent);
        BuildMenuSlotView view = slotObject.GetComponent<BuildMenuSlotView>();
        if (view == null) view = slotObject.AddComponent<BuildMenuSlotView>();

        view.Bind(definition, true, string.Empty,
            tooltip,
            () => GameEvents.TriggerBuildDefinitionSelected(
                definition.DefinitionId,
                definition.TileType,
                definition.PlacementLayer));
    }

    private void CreateLegacySlots()
    {
        foreach (BuildRecipe recipe in buildRecipes)
        {
            if (currentFilter.HasValue && recipe.category != currentFilter.Value) continue;
            if (!MatchesSearch(recipe.displayName)) continue;
            if (RecipeUnlockService.Instance != null
                && !RecipeUnlockService.Instance.IsUnlocked(recipe.tileType)) continue;

            ResourceType resource = BuildingCosts.GetRequiredResource(recipe.tileType);
            if (StockpileManager.Instance == null
                || !StockpileManager.Instance.HasUnlockedResource(resource)) continue;

            GameObject slot = Instantiate(itemSlotPrefab, contentParent);
            BuildMenuSlotView view = slot.GetComponent<BuildMenuSlotView>();
            if (view == null) view = slot.AddComponent<BuildMenuSlotView>();
            view.BindLegacy(recipe.displayName, recipe.icon, resource,
                BuildingCosts.GetCost(recipe.tileType),
                () => GameEvents.TriggerBuildRecipeSelected(recipe.tileType, recipe.placementLayer));
        }
    }

    private bool MatchesCurrentView(BuildDefinitionSO definition)
    {
        return (!currentFilter.HasValue || definition.Category == currentFilter.Value)
            && MatchesSearch(definition.DisplayName);
    }

    private static List<BuildDefinitionSO> CollectVisibleDefinitions(
        IReadOnlyList<BuildDefinitionSO> definitions)
    {
        List<BuildDefinitionSO> visible = new List<BuildDefinitionSO>();

        foreach (BuildDefinitionSO definition in definitions)
        {
            if (definition == null) continue;

            bool recipeUnlocked = RecipeUnlockService.Instance == null
                || RecipeUnlockService.Instance.IsUnlocked(definition);
            bool resourceUnlocked = StockpileManager.Instance != null
                && StockpileManager.Instance.HasUnlockedResource(
                    definition.RequiredResource);

            if (recipeUnlocked && resourceUnlocked) visible.Add(definition);
        }

        return visible;
    }

    private bool MatchesSearch(string displayName)
    {
        return string.IsNullOrEmpty(searchTerm)
            || (!string.IsNullOrEmpty(displayName)
                && displayName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent) Destroy(child.gameObject);
    }

    private static int CompareDefinitions(BuildDefinitionSO left, BuildDefinitionSO right)
    {
        // MenuSortOrder é opcional. Valores positivos formam uma seção manual;
        // zero significa "ordene automaticamente pelo nome".
        if (left.HasCustomMenuSortOrder != right.HasCustomMenuSortOrder)
            return left.HasCustomMenuSortOrder ? -1 : 1;

        if (left.HasCustomMenuSortOrder)
        {
            int byOrder = left.MenuSortOrder.CompareTo(right.MenuSortOrder);
            if (byOrder != 0) return byOrder;
        }

        int byName = string.Compare(left.DisplayName, right.DisplayName,
            StringComparison.OrdinalIgnoreCase);
        if (byName != 0) return byName;

        return string.Compare(left.DefinitionId, right.DefinitionId, StringComparison.Ordinal);
    }

}
