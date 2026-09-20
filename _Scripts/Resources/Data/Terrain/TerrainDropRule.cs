using System;
using UnityEngine;

[Serializable]
public sealed class TerrainDropRule
{
    [SerializeField] private ResourceType resourceType;
    [SerializeField, Range(0f, 1f)] private float chance = 1f;
    [SerializeField, Min(0)] private int minimumAmount = 1;
    [SerializeField, Min(0)] private int maximumAmount = 1;

    public ResourceType ResourceType => resourceType;
    public float Chance => Mathf.Clamp01(chance);
    public int MinimumAmount => Mathf.Max(0, minimumAmount);
    public int MaximumAmount => Mathf.Max(MinimumAmount, maximumAmount);

    public bool TryRoll(
        out TerrainDropResult result,
        float chanceMultiplier = 1f,
        float amountMultiplier = 1f)
    {
        result = default;
        float effectiveChance = Mathf.Clamp01(
            Chance * Mathf.Max(0f, chanceMultiplier));
        if (MaximumAmount <= 0
            || effectiveChance <= 0f
            || (effectiveChance < 1f
                && UnityEngine.Random.value >= effectiveChance))
            return false;

        int amount = UnityEngine.Random.Range(MinimumAmount, MaximumAmount + 1);
        amount = Mathf.RoundToInt(amount * Mathf.Max(0f, amountMultiplier));
        if (amount <= 0) return false;

        result = new TerrainDropResult(resourceType, amount);
        return true;
    }

    public void Validate()
    {
        chance = Mathf.Clamp01(chance);
        minimumAmount = Mathf.Max(0, minimumAmount);
        maximumAmount = Mathf.Max(minimumAmount, maximumAmount);
    }
}

public readonly struct TerrainDropResult
{
    public ResourceType ResourceType { get; }
    public int Amount { get; }

    public TerrainDropResult(ResourceType resourceType, int amount)
    {
        ResourceType = resourceType;
        Amount = Mathf.Max(0, amount);
    }
}
