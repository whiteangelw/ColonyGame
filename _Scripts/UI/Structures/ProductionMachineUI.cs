using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ProductionMachineUI : MonoBehaviour
{
    public static ProductionMachineUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Dropdown recipeDropdown;
    [SerializeField] private Slider batchSlider;
    [SerializeField] private TMP_Text batchAmountText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Button startButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button closeButton;

    private ProductionMachineBehaviour currentMachine;
    private readonly List<ProductionRecipeSO> visibleRecipes =
        new List<ProductionRecipeSO>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        panel?.SetActive(false);
    }

    private void OnEnable()
    {
        recipeDropdown?.onValueChanged.AddListener(HandleRecipeChanged);
        batchSlider?.onValueChanged.AddListener(HandleBatchChanged);
        startButton?.onClick.AddListener(HandleStartClicked);
        cancelButton?.onClick.AddListener(HandleCancelClicked);
        closeButton?.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        recipeDropdown?.onValueChanged.RemoveListener(HandleRecipeChanged);
        batchSlider?.onValueChanged.RemoveListener(HandleBatchChanged);
        startButton?.onClick.RemoveListener(HandleStartClicked);
        cancelButton?.onClick.RemoveListener(HandleCancelClicked);
        closeButton?.onClick.RemoveListener(Close);
        UnsubscribeMachine();
    }

    public void Open(ProductionMachineBehaviour machine)
    {
        if (machine == null) return;
        UnsubscribeMachine();
        currentMachine = machine;
        currentMachine.StateChanged += Refresh;
        BuildRecipeOptions();
        panel?.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        UnsubscribeMachine();
        panel?.SetActive(false);
    }

    private void BuildRecipeOptions()
    {
        visibleRecipes.Clear();
        recipeDropdown?.ClearOptions();
        if (currentMachine == null) return;

        List<string> labels = new List<string>();
        IReadOnlyList<ProductionRecipeSO> recipes = currentMachine.AvailableRecipes;
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i] == null) continue;
            visibleRecipes.Add(recipes[i]);
            labels.Add(recipes[i].DisplayName);
        }
        recipeDropdown?.AddOptions(labels);
    }

    private void Refresh()
    {
        if (currentMachine == null) return;
        if (titleText != null) titleText.text = currentMachine.name;

        bool busy = currentMachine.HasActiveOrder;
        bool pending = currentMachine.HasPendingOrder;
        if (recipeDropdown != null) recipeDropdown.interactable = !busy;
        if (batchSlider != null) batchSlider.interactable = !busy;
        if (startButton != null) startButton.interactable = !busy && visibleRecipes.Count > 0;
        if (cancelButton != null) cancelButton.interactable = busy || pending;

        if (batchAmountText != null && batchSlider != null)
        {
            batchAmountText.text = Mathf.RoundToInt(batchSlider.value).ToString();
        }

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = Mathf.Max(0.1f, currentMachine.WorkRequired);
            progressSlider.value = currentMachine.CompletedWork;
        }

        if (statusText != null)
        {
            statusText.text = pending
                ? "Confirmando ordem..."
                : busy
                ? currentMachine.CanOperate
                    ? $"Operando — {currentMachine.RemainingBatches} lote(s) restante(s)"
                    : $"Aguardando materiais — {currentMachine.RemainingBatches} lote(s)"
                : "Sem ordem de produção";
        }
    }

    private void HandleStartClicked()
    {
        if (currentMachine == null || visibleRecipes.Count == 0) return;
        int index = recipeDropdown != null
            ? Mathf.Clamp(recipeDropdown.value, 0, visibleRecipes.Count - 1)
            : 0;
        int batches = batchSlider != null
            ? Mathf.Max(1, Mathf.RoundToInt(batchSlider.value))
            : 1;
        currentMachine.RequestOrder(visibleRecipes[index], batches);
        Refresh();
    }

    private void HandleCancelClicked()
    {
        if (currentMachine == null) return;
        if (currentMachine.HasActiveOrder) currentMachine.CancelOrder();
        else currentMachine.CancelPendingOrder();
        Refresh();
    }

    private void HandleRecipeChanged(int _) => Refresh();
    private void HandleBatchChanged(float _) => Refresh();

    private void UnsubscribeMachine()
    {
        if (currentMachine != null)
        {
            currentMachine.StateChanged -= Refresh;
        }
        currentMachine = null;
    }
}
