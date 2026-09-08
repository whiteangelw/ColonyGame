using UnityEngine;

public enum RewardCardType
{
    Resource,
    Duplicant,
    Recipe,
    Bonus
}

public enum RewardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

[CreateAssetMenu(
    fileName = "NewRewardCard",
    menuName = "Rewards/Reward Card"
)]
public class RewardCardDefinition : ScriptableObject
{
    [Header("Apresentação")]
    public string cardId;
    public string displayName;
    [TextArea(2, 4)] public string description;
    public Sprite icon;

    [Header("Sorteio")]
    public RewardCardType cardType;
    public RewardRarity rarity = RewardRarity.Common;
    [Min(0.01f)] public float selectionWeight = 1f;

    [Header("Recompensa de recurso")]
    public ResourceType resourceType;
    [Min(1)] public int resourceAmount = 1;

    [Header("Recompensa de duplicant")]
    public DuplicantDefinition duplicantDefinition;

    [Header("Recompensa de receita")]
    [Tooltip("Definition Id do BuildDefinitionSO. Use este campo em cartas novas.")]
    public string recipeDefinitionId;

    [Tooltip("Compatibilidade com cartas antigas. Ignorado quando o ID acima está preenchido.")]
    public TileType recipeTileType;

    public bool HasRecipeDefinitionId =>
        !string.IsNullOrWhiteSpace(recipeDefinitionId);

    public float GetWeightedChance()
    {
        return Mathf.Max(0.01f, selectionWeight)
            * GetRarityMultiplier();
    }

    public Color GetRarityColor()
    {
        switch (rarity)
        {
            case RewardRarity.Uncommon: return new Color(0.35f, 0.85f, 0.4f);
            case RewardRarity.Rare: return new Color(0.3f, 0.55f, 1f);
            case RewardRarity.Epic: return new Color(0.7f, 0.35f, 1f);
            case RewardRarity.Legendary: return new Color(1f, 0.65f, 0.15f);
            default: return Color.white;
        }
    }

    private float GetRarityMultiplier()
    {
        switch (rarity)
        {
            case RewardRarity.Uncommon: return 0.6f;
            case RewardRarity.Rare: return 0.25f;
            case RewardRarity.Epic: return 0.1f;
            case RewardRarity.Legendary: return 0.03f;
            default: return 1f;
        }
    }
}
