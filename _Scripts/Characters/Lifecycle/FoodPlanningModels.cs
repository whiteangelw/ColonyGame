using System;
using System.Collections.Generic;
using UnityEngine;

public enum FoodSourceKind
{
    Storage,
    GroundItem,
    Flora
}

public enum FoodSearchFailureReason
{
    None,
    SearchDeferred,
    NoFoodAvailable,
    FoodUnreachable,
    FoodJourneyUnsafe,
    FoodReservationFailed
}

[Serializable]
public struct FoodOption
{
    public ResourceType resourceType;
    public int availablePortions;
    public float hungerRestoredPerPortion;
    public bool isRawFood;
    public int quality;

    public bool IsValid => availablePortions > 0
        && hungerRestoredPerPortion > 0f;
}

public sealed class MealPlan
{
    public IFoodSource Source { get; }
    public ResourceType FoodType { get; }
    public int ReservedPortions { get; }
    public float HungerTarget { get; }
    public float ExpectedRestoration { get; }
    public float EstimatedTravelSeconds { get; }
    public float EstimatedHungerOnArrival { get; }
    public bool IsRawFood { get; }
    public Vector2Int InteractionPosition { get; }
    public List<Vector2Int> Path { get; }

    public MealPlan(
        IFoodSource source,
        ResourceType foodType,
        int reservedPortions,
        float hungerTarget,
        float expectedRestoration,
        float estimatedTravelSeconds,
        float estimatedHungerOnArrival,
        bool isRawFood,
        Vector2Int interactionPosition,
        List<Vector2Int> path)
    {
        Source = source;
        FoodType = foodType;
        ReservedPortions = reservedPortions;
        HungerTarget = hungerTarget;
        ExpectedRestoration = expectedRestoration;
        EstimatedTravelSeconds = estimatedTravelSeconds;
        EstimatedHungerOnArrival = estimatedHungerOnArrival;
        IsRawFood = isRawFood;
        InteractionPosition = interactionPosition;
        Path = path;
    }
}
