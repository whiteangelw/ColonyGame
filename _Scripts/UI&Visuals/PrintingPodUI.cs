using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PrintingPodUI : MonoBehaviour
{
    [Header("Componentes UI")]
    [SerializeField] private Button printButton;
    [SerializeField] private TextMeshProUGUI timerText;
    private void OnEnable() => GameEvents.OnPrintingPodStateChanged += UpdateDisplay;
    private void OnDisable() => GameEvents.OnPrintingPodStateChanged -= UpdateDisplay;
    private void Start()
    {
        if (printButton != null)
        {
            printButton.onClick.RemoveAllListeners();
            printButton.onClick.AddListener(OnPrintButtonClicked);
        }
    }

    private void UpdateDisplay(int availablePrints, float timeRemaining)
    {
        bool canPrint = availablePrints > 0;
        if (printButton != null) printButton.interactable = canPrint;

        if (timerText != null)
        {
            timerText.text = canPrint ? $"IMPRIMIR! ({availablePrints})" : $"Aguarde: {Mathf.CeilToInt(timeRemaining)}s";
        }
    }

    private void OnPrintButtonClicked()
    {
        if (PrintingPod.Instance != null)
        {
            PrintingPod.Instance.PrintDuplicant();
        }
    }
}