using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RewardCardView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image rarityBorder;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI rarityText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Button chooseButton;

    private RewardCardDefinition boundCard;
    private Action<RewardCardDefinition> onSelected;

    public void Bind(
        RewardCardDefinition card,
        Action<RewardCardDefinition> selectionCallback)
    {
        boundCard = card;
        onSelected = selectionCallback;

        if (iconImage != null)
        {
            iconImage.sprite = card.icon;
            iconImage.enabled = card.icon != null;
        }

        if (nameText != null) nameText.text = card.displayName;
        if (rarityText != null) rarityText.text = card.rarity.ToString();
        if (descriptionText != null) descriptionText.text = card.description;
        if (rarityBorder != null) rarityBorder.color = card.GetRarityColor();

        if (chooseButton != null)
        {
            chooseButton.onClick.RemoveAllListeners();
            chooseButton.onClick.AddListener(SelectBoundCard);
            chooseButton.interactable = true;
        }

        gameObject.SetActive(true);
    }

    public void SetInteractable(bool interactable)
    {
        if (chooseButton != null)
        {
            chooseButton.interactable = interactable;
        }
    }

    private void SelectBoundCard()
    {
        if (boundCard != null)
        {
            onSelected?.Invoke(boundCard);
        }
    }
}
