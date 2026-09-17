using TMPro;
using UnityEngine;

public sealed class BuildMenuTooltipUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    private void Awake()
    {
        Hide();
    }

    public void Show(BuildDefinitionSO definition, bool isUnlocked, string lockReason)
    {
        if (definition == null) return;

        if (panel != null) panel.SetActive(true);
        else gameObject.SetActive(true);

        if (titleText != null) titleText.text = definition.DisplayName;
        if (bodyText == null) return;

        StructureFootprintDefinition footprint = definition.GetFootprint();
        string description = string.IsNullOrWhiteSpace(definition.Description)
            ? "Sem descrição."
            : definition.Description;
        string state = isUnlocked ? "Disponível" : "Bloqueado: " + lockReason;

        bodyText.text = description
            + "\n\nCusto: " + definition.Cost + " " + definition.RequiredResource
            + "\nTamanho: " + footprint.width + "×" + footprint.height
            + "\nEstado: " + state;
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        else gameObject.SetActive(false);
    }
}
