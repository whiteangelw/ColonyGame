using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlanterInspectUI : MonoBehaviour
{
    public static PlanterInspectUI Instance { get; private set; }

    [SerializeField] private GameObject panelContainer;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Transform cropOptionsRoot;
    [SerializeField] private Button cropOptionButtonPrefab;
    [SerializeField] private Button closeButton;

    private PlanterStructureBehaviour selectedPlanter;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        if (panelContainer != null) panelContainer.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(PlanterStructureBehaviour planter)
    {
        if (planter == null) return;

        selectedPlanter = planter;
        if (panelContainer != null) panelContainer.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        selectedPlanter = null;
        ClearCropButtons();
        if (panelContainer != null) panelContainer.SetActive(false);
    }

    private void Refresh()
    {
        ClearCropButtons();
        if (selectedPlanter == null) return;

        if (titleText != null) titleText.text = "Plantadeira";
        if (statusText != null)
        {
            statusText.text = BuildStatusText(selectedPlanter);
        }

        if (selectedPlanter.State != PlanterState.Empty
            || selectedPlanter.Settings == null
            || cropOptionsRoot == null
            || cropOptionButtonPrefab == null)
        {
            return;
        }

        foreach (FloraDefinitionSO crop in
                 selectedPlanter.Settings.AvailableCrops)
        {
            if (crop == null) continue;
            CreateCropButton(crop);
        }

        FloraDefinitionSO legacyDefault =
            selectedPlanter.Settings.CropDefinition;
        if (legacyDefault != null
            && !ContainsCrop(
                selectedPlanter.Settings.AvailableCrops,
                legacyDefault))
        {
            CreateCropButton(legacyDefault);
        }
    }

    private void CreateCropButton(FloraDefinitionSO crop)
    {
        Button button = Instantiate(cropOptionButtonPrefab, cropOptionsRoot);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = crop.floraId;
        button.onClick.AddListener(() => SelectCrop(crop));
    }

    private void SelectCrop(FloraDefinitionSO crop)
    {
        if (selectedPlanter != null
            && selectedPlanter.TrySelectCrop(crop))
        {
            Refresh();
        }
    }

    private static string BuildStatusText(PlanterStructureBehaviour planter)
    {
        switch (planter.State)
        {
            case PlanterState.Growing:
                return $"Crescendo: {Mathf.CeilToInt(planter.RemainingGrowthTime)}s";
            case PlanterState.Ready:
                return "Pronta para colheita";
            default:
                return "Vazia — escolha uma planta";
        }
    }

    private void ClearCropButtons()
    {
        if (cropOptionsRoot == null) return;
        for (int i = cropOptionsRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(cropOptionsRoot.GetChild(i).gameObject);
        }
    }

    private static bool ContainsCrop(
        System.Collections.Generic.IReadOnlyList<FloraDefinitionSO> crops,
        FloraDefinitionSO target)
    {
        if (crops == null || target == null) return false;
        for (int i = 0; i < crops.Count; i++)
        {
            if (crops[i] == target) return true;
        }

        return false;
    }
}
