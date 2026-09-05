using UnityEngine;

[CreateAssetMenu(
    fileName = "LifeCycleSettings",
    menuName = "Colony/Life Cycle Settings"
)]
public class LifeCycleSettingsSO : ScriptableObject
{
    [Header("Ticks escalonados")]
    [Min(0.1f)] public float tickInterval = 1f;
    [Min(1)] public int maximumDuplicantsPerFrame = 5;

    [Header("Valores iniciais")]
    [Range(0f, 1f)] public float initialEnergyPercent = 1f;
    [Range(0f, 1f)] public float initialHungerPercent = 1f;

    [Header("Consumo por segundo")]
    [Min(0f)] public float hungerDrain = 0.08f;
    [Min(0f)] public float idleEnergyDrain = 0.03f;
    [Min(0f)] public float movingEnergyDrain = 0.08f;
    [Min(0f)] public float workingEnergyDrain = 0.14f;

    [Header("Limites de estado")]
    [Range(0f, 1f)] public float hungryThreshold = 0.35f;
    [Range(0f, 1f)] public float starvingThreshold = 0.10f;
    [Range(0f, 1f)] public float tiredThreshold = 0.30f;
    [Range(0f, 1f)] public float exhaustedThreshold = 0.10f;

    [Header("Soneca de emergência")]
    [Min(0.1f)] public float emergencyNapDuration = 5f;
    [Range(0.01f, 1f)] public float emergencyNapRecoveryPercent = 0.20f;

    [Header("Planejamento de refeições")]
    [Range(0f, 1f)] public float preventiveMealTarget = 0.75f;
    [Range(0f, 1f)] public float criticalMealTarget = 0.95f;
    [Range(0f, 1f)] public float emergencyMealTarget = 1f;
    [Range(0f, 1f)] public float rawFoodAllowedBelow = 0.10f;
    [Range(0f, 1f)] public float safeArrivalHungerMargin = 0.10f;
    [Min(1)] public int preventiveFoodSearchRadius = 120;
    [Min(1)] public int criticalFoodSearchRadius = 80;
    [Min(1)] public int emergencyFoodSearchRadius = 40;
    [Min(1)] public int maximumFoodPathChecks = 8;
    [Min(1)] public int maximumFoodSearchesPerFrame = 2;
    [Min(1f)] public float travelTimeSafetyMultiplier = 1.35f;
    [Min(0.1f)] public float foodEatingDurationPerPortion = 1.2f;
    [Min(0.1f)] public float foodSearchRetryDelay = 3f;

    [Header("Efeito de alimento cru")]
    [Range(0f, 1f)] public float rawFoodDiscomfortChance = 1f;
    [Min(0f)] public float rawFoodDiscomfortDuration = 60f;
    [Range(0f, 0.95f)] public float rawFoodWorkPenaltyPercent = 0.15f;
}
