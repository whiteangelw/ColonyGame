using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PrintingPodUI : MonoBehaviour
{
    [Header("Componentes UI")]
    [SerializeField] private Button printButton;
    [SerializeField] private TextMeshProUGUI timerText;

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
            printButton.gameObject.SetActive(false);
        }

        RefreshDisplay();
    }

    private void HandlePrintingPodBuilt(PrintingPod printingPod)
    {
        RefreshDisplay();
    }

    private void HandlePrintingPodRemoved(PrintingPod printingPod)
    {
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (timerText == null)
        {
            return;
        }

        bool dropsEnabled = PrintingPod.Instance != null
            && PrintingPod.Instance.IsOperational;

        timerText.text = dropsEnabled
            ? "Drops ativados!"
            : "Construa a máquina para ativar os drops.";
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
            timerText.text = "Construa a máquina para ativar os drops.";
            return;
        }

        timerText.text = offerReady
            ? "Nova oferta disponível!"
            : $"Próximo drop: {Mathf.CeilToInt(timeRemaining)}s";
    }
}
