using System;

[Serializable]
public readonly struct ResourceAmount
{
    public ResourceType ResourceType { get; }
    public int Amount { get; }

    public ResourceAmount(ResourceType resourceType, int amount)
    {
        ResourceType = resourceType;
        Amount = Math.Max(0, amount);
    }
}
