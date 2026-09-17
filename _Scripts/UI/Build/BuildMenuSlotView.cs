using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class BuildMenuSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private TextMeshProUGUI lockedText;

    private BuildDefinitionSO definition;
    private BuildMenuTooltipUI tooltip;
    private bool isUnlocked;
    private string lockReason;

    public void Bind(BuildDefinitionSO buildDefinition, bool unlocked,
        string lockedReason, BuildMenuTooltipUI tooltipUI, Action onClick)
    {
        definition = buildDefinition;
        tooltip = tooltipUI;
        isUnlocked = unlocked;
        lockReason = lockedReason;
        ResolveReferences();

        if (labelText != null)
        {
            labelText.text = buildDefinition.DisplayName + "\n<size=80%>("
                + buildDefinition.RequiredResource + ": " + buildDefinition.Cost + ")</size>";
        }

        if (iconImage != null && buildDefinition.Icon != null) iconImage.sprite = buildDefinition.Icon;
        ApplyLockedVisual(unlocked, lockedReason);

        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.interactable = unlocked;
        if (unlocked && onClick != null) button.onClick.AddListener(() => onClick());
    }

    public void BindLegacy(string displayName, Sprite icon, ResourceType resource,
        int cost, Action onClick)
    {
        definition = null;
        tooltip = null;
        ResolveReferences();

        if (labelText != null)
        {
            labelText.text = displayName + "\n<size=80%>(" + resource + ": " + cost + ")</size>";
        }

        if (iconImage != null && icon != null) iconImage.sprite = icon;
        ApplyLockedVisual(true, string.Empty);

        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.interactable = true;
        if (onClick != null) button.onClick.AddListener(() => onClick());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (definition != null && tooltip != null) tooltip.Show(definition, isUnlocked, lockReason);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltip != null) tooltip.Hide();
    }

    private void ResolveReferences()
    {
        if (button == null) button = GetComponent<Button>();
        if (labelText == null) labelText = GetComponentInChildren<TextMeshProUGUI>();
        if (iconImage != null) return;

        Transform iconTransform = transform.Find("Icon");
        if (iconTransform != null) iconImage = iconTransform.GetComponent<Image>();
    }

    private void ApplyLockedVisual(bool unlocked, string reason)
    {
        if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);
        if (lockedText != null) lockedText.text = reason;

        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = unlocked ? 1f : 0.55f;
    }
}
