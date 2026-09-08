using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PrintingPodUI : MonoBehaviour
{
    [Header("Componentes UI")]
    [Tooltip("CanvasGroup usado para ocultar a UI sem desativar este GameObject.")]
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private Button printButton;
    [SerializeField] private TextMeshProUGUI timerText;

    private void Awake()
    {
        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        GameEvents.OnPrintingPodBuilt += HandlePrintingPodBuilt;
        GameEvents.OnPrintingPodRemoved += HandlePrintingPodRemoved;
        GameEvents.OnPrintingPodTimerChanged += HandleTimerChanged;
        RefreshDisplay();
    }

    private void OnDisable()
    {
        GameEvents.OnPrintingPodBuilt -= HandlePrintingPodBuilt;
        GameEvents.OnPrintingPodRemoved -= HandlePrintingPodRemoved;
        GameEvents.OnPrintingPodTimerChanged -= HandleTimerChanged;
    }

    private void Start()
    {
        if (printButton != null)
        {
            printButton.onClick.RemoveAllListeners();
            printButton.onClick.AddListener(OpenRewardChoices);
            printButton.gameObject.SetActive(false);
        }

        RefreshDisplay();
    }

    private void HandlePrintingPodBuilt(PrintingPod printingPod)
    {
        SetPanelVisible(true);
        RefreshDisplay();
    }

    private void HandlePrintingPodRemoved(PrintingPod printingPod)
    {
        SetOfferButtonVisible(false);
        SetPanelVisible(false);
    }

    private void RefreshDisplay()
    {
        if (timerText == null)
        {
            return;
        }

        bool dropsEnabled = PrintingPod.Instance != null
            && PrintingPod.Instance.IsOperational;

        SetPanelVisible(dropsEnabled);

        if (dropsEnabled)
        {
            timerText.text = "Drops ativados!";
        }

        SetOfferButtonVisible(
            dropsEnabled
            && PrintingPodScheduler.Instance != null
            && PrintingPodScheduler.Instance.IsOfferReady
        );
    }

    private void HandleTimerChanged(float timeRemaining, bool offerReady)
    {
        if (timerText == null)
        {
            return;
        }

        if (PrintingPod.Instance == null
            || !PrintingPod.Instance.IsOperational)
        {
            SetPanelVisible(false);
            SetOfferButtonVisible(false);
            return;
        }

        SetPanelVisible(true);

        timerText.text = offerReady
            ? "Nova oferta disponível!"
            : $"Próximo drop: {Mathf.CeilToInt(timeRemaining)}s";

        SetOfferButtonVisible(offerReady);
    }

    private void OpenRewardChoices()
    {
        RewardChoiceUI.Instance?.OpenCurrentOffer();
    }

    private void SetOfferButtonVisible(bool visible)
    {
        if (printButton != null)
        {
            printButton.gameObject.SetActive(visible);
            printButton.interactable = visible;
        }
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelCanvasGroup == null)
        {
            return;
        }

        panelCanvasGroup.alpha = visible ? 1f : 0f;
        panelCanvasGroup.interactable = visible;
        panelCanvasGroup.blocksRaycasts = visible;
    }
}
