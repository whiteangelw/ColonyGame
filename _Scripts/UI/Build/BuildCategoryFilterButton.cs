using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BuildCategoryFilterButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private CanvasGroup canvasGroup;

    public void Bind(string label, BuildCategory? category,
        BuildCategory? selectedCategory, Action<BuildCategory?> onSelected)
    {
        ResolveReferences();
        if (labelText != null) labelText.text = label;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = category == selectedCategory ? 1f : 0.65f;
        }

        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onSelected(category));
    }

    private void ResolveReferences()
    {
        if (button == null) button = GetComponent<Button>();
        if (labelText == null) labelText = GetComponentInChildren<TextMeshProUGUI>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }
}
