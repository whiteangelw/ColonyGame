using UnityEngine;
using TMPro;

public class StockpileUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI dirtText;
    [SerializeField] private TextMeshProUGUI stoneText;
    [SerializeField] private TextMeshProUGUI copperText;
    [SerializeField] private TextMeshProUGUI ironText;

    private void OnEnable()
    {
        GameEvents.OnResourceAmountChanged += UpdateResourceDisplay;
    }

    private void OnDisable()
    {
        GameEvents.OnResourceAmountChanged -= UpdateResourceDisplay;
    }

    private void Start()
    {
        if (StockpileManager.Instance != null)
        {
            UpdateResourceDisplay(ResourceType.Dirt, StockpileManager.Instance.GetAmount(ResourceType.Dirt));
            UpdateResourceDisplay(ResourceType.Stone, StockpileManager.Instance.GetAmount(ResourceType.Stone));
            UpdateResourceDisplay(ResourceType.Copper, StockpileManager.Instance.GetAmount(ResourceType.Copper));
            UpdateResourceDisplay(ResourceType.Iron, StockpileManager.Instance.GetAmount(ResourceType.Iron));
        }
    }

    private void UpdateResourceDisplay(ResourceType type, int amount)
    {
        switch (type)
        {
            case ResourceType.Dirt:
                if (dirtText != null) dirtText.text = $"Terra: {amount}";
                break;
            case ResourceType.Stone:
                if (stoneText != null) stoneText.text = $"Pedra: {amount}";
                break;
            case ResourceType.Copper:
                if (copperText != null) copperText.text = $"Cobre: {amount}";
                break;
            case ResourceType.Iron:
                if (ironText != null) ironText.text = $"Ferro: {amount}";
                break;
        }
    }
}