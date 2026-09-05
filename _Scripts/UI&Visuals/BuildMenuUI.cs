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
    }

    private void OnDisable()
    {
        GameEvents.OnToggleBuildMenuRequested -= HandleToggleMenuRequested;
        GameEvents.OnResourceUnlocked -= OnNewResourceUnlocked;
        GameEvents.OnRecipeUnlocked -= OnRecipeUnlocked;
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

        foreach (var recipe in buildRecipes)
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

            GameObject slotObj = Instantiate(itemSlotPrefab, contentParent);

            TextMeshProUGUI labelText = slotObj.GetComponentInChildren<TextMeshProUGUI>();
            Image iconImage = slotObj.transform.Find("Icon")?.GetComponent<Image>();
            Button btn = slotObj.GetComponent<Button>();

            int cost = BuildingCosts.GetCost(recipe.tileType);

            if (labelText != null)
            {
                labelText.text = $"{recipe.displayName}\n<size=80%>({reqResource}: {cost})</size>";
            }

            if (iconImage != null && recipe.icon != null)
            {
                iconImage.sprite = recipe.icon;
            }

            TileType selectedTile = recipe.tileType;
            GridLayer selectedLayer = recipe.placementLayer;
            if (btn != null)
            {
                btn.onClick.AddListener(() => SelectTile(selectedTile, selectedLayer));
            }
        }
    }

    private void SelectTile(TileType type, GridLayer layer)
    {
        GameEvents.TriggerBuildRecipeSelected(type, layer);
    }
}
