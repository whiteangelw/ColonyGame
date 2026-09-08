using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildMenuUI : MonoBehaviour
{
    [Header("Container Principal da UI")]
    [SerializeField] private GameObject menuPanelContainer;

    [Header("Configuração dos Slots")]
    [SerializeField] private GameObject itemSlotPrefab;
    [SerializeField] private Transform contentParent;

    [Header("Botões de Filtro")]
    [SerializeField] private Button btnFilterAll;
    [SerializeField] private Button btnFilterBlocks;
    [SerializeField] private Button btnFilterStorage;
    [SerializeField] private Button btnFilterMachines;

    [Header("Catálogo de Construções")]
    [SerializeField] private List<BuildRecipe> buildRecipes = new List<BuildRecipe>();

    private BuildCategory? currentFilter = null;

    private void Awake()
    {
        if (menuPanelContainer != null)
        {
            menuPanelContainer.SetActive(false);
        }
    }

    private void Start()
    {
        if (btnFilterAll != null) btnFilterAll.onClick.AddListener(() => SetFilter(null));
        if (btnFilterBlocks != null) btnFilterBlocks.onClick.AddListener(() => SetFilter(BuildCategory.Blocks));
        if (btnFilterStorage != null) btnFilterStorage.onClick.AddListener(() => SetFilter(BuildCategory.Storage));
        if (btnFilterMachines != null) btnFilterMachines.onClick.AddListener(() => SetFilter(BuildCategory.Machines));

        RefreshMenu();
    }

    private void OnEnable()
    {
        GameEvents.OnToggleBuildMenuRequested += HandleToggleMenuRequested;
        GameEvents.OnResourceUnlocked += OnNewResourceUnlocked;
        GameEvents.OnRecipeUnlocked += OnRecipeUnlocked;
        GameEvents.OnBuildDefinitionUnlocked += OnBuildDefinitionUnlocked;
        GameEvents.OnRecipeUnlockStateChanged += RefreshMenu;
    }

    private void OnDisable()
    {
        GameEvents.OnToggleBuildMenuRequested -= HandleToggleMenuRequested;
        GameEvents.OnResourceUnlocked -= OnNewResourceUnlocked;
        GameEvents.OnRecipeUnlocked -= OnRecipeUnlocked;
        GameEvents.OnBuildDefinitionUnlocked -= OnBuildDefinitionUnlocked;
        GameEvents.OnRecipeUnlockStateChanged -= RefreshMenu;
    }

    private void HandleToggleMenuRequested(bool shouldOpen)
    {
        if (menuPanelContainer != null)
        {
            menuPanelContainer.SetActive(shouldOpen);
            if (shouldOpen)
            {
                RefreshMenu();
            }
        }
    }

    private void OnNewResourceUnlocked(ResourceType unlockedType)
    {
        RefreshMenu();
    }

    private void OnRecipeUnlocked(TileType tileType)
    {
        RefreshMenu();
    }

    private void OnBuildDefinitionUnlocked(string definitionId)
    {
        RefreshMenu();
    }

    public void SetFilter(BuildCategory? category)
    {
        currentFilter = category;
        RefreshMenu();
    }

    public void RefreshMenu()
    {
        if (contentParent == null || itemSlotPrefab == null) return;

        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        IReadOnlyList<BuildDefinitionSO> definitions =
            BuildCatalogService.Instance?.Definitions;

        if (definitions != null && definitions.Count > 0)
        {
            foreach (BuildDefinitionSO definition in definitions)
            {
                if (definition == null) continue;
                CreateDefinitionSlot(definition);
            }

            return;
        }

        foreach (BuildRecipe recipe in buildRecipes)
        {
            if (RecipeUnlockService.Instance != null
                && !RecipeUnlockService.Instance.IsUnlocked(recipe.tileType))
            {
                continue;
            }

            ResourceType reqResource = BuildingCosts.GetRequiredResource(recipe.tileType);

            bool isUnlocked = StockpileManager.Instance != null && StockpileManager.Instance.HasUnlockedResource(reqResource);
            if (!isUnlocked) continue;

            if (currentFilter.HasValue && recipe.category != currentFilter.Value) continue;

            CreateLegacySlot(recipe, reqResource);
        }
    }

    private void CreateDefinitionSlot(BuildDefinitionSO definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DefinitionId))
        {
            Debug.LogWarning(
                $"[BuildMenuUI] Definição sem ID ignorada: {definition.name}",
                definition);
            return;
        }

        if (RecipeUnlockService.Instance != null
            && !RecipeUnlockService.Instance.IsUnlocked(definition))
        {
            return;
        }

        if (StockpileManager.Instance == null
            || !StockpileManager.Instance.HasUnlockedResource(
                definition.RequiredResource))
        {
            return;
        }

        if (currentFilter.HasValue
            && definition.Category != currentFilter.Value)
        {
            return;
        }

        GameObject slotObject = Instantiate(itemSlotPrefab, contentParent);
        ConfigureSlot(
            slotObject,
            definition.DisplayName,
            definition.RequiredResource,
            definition.Cost,
            definition.Icon,
            () => GameEvents.TriggerBuildDefinitionSelected(
                definition.DefinitionId,
                definition.TileType,
                definition.PlacementLayer));
    }

    private void CreateLegacySlot(
        BuildRecipe recipe,
        ResourceType requiredResource)
    {
        GameObject slotObject = Instantiate(itemSlotPrefab, contentParent);
        ConfigureSlot(
            slotObject,
            recipe.displayName,
            requiredResource,
            BuildingCosts.GetCost(recipe.tileType),
            recipe.icon,
            () => SelectTile(recipe.tileType, recipe.placementLayer));
    }

    private static void ConfigureSlot(
        GameObject slotObject,
        string displayName,
        ResourceType requiredResource,
        int cost,
        Sprite icon,
        UnityEngine.Events.UnityAction onClick)
    {
        TextMeshProUGUI labelText =
            slotObject.GetComponentInChildren<TextMeshProUGUI>();
        Image iconImage = slotObject.transform.Find("Icon")?.GetComponent<Image>();
        Button button = slotObject.GetComponent<Button>();

        if (labelText != null)
        {
            labelText.text =
                $"{displayName}\n<size=80%>({requiredResource}: {cost})</size>";
        }

        if (iconImage != null && icon != null) iconImage.sprite = icon;
        if (button != null) button.onClick.AddListener(onClick);
    }

    private void SelectTile(TileType type, GridLayer layer)
    {
        GameEvents.TriggerBuildRecipeSelected(type, layer);
    }
}
