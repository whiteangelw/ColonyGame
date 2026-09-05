using UnityEngine;

[DisallowMultipleComponent]
public class DuplicantStatusEffects : MonoBehaviour
{
    [SerializeField] private float rawFoodDiscomfortRemaining;
    [SerializeField, Range(0f, 0.95f)] private float workPenaltyPercent;

    public bool HasRawFoodDiscomfort => rawFoodDiscomfortRemaining > 0f;
    public float RawFoodDiscomfortRemaining => rawFoodDiscomfortRemaining;
    public float WorkPenaltyPercent => HasRawFoodDiscomfort
        ? workPenaltyPercent
        : 0f;
    public float WorkEfficiencyMultiplier => Mathf.Clamp01(
        1f - WorkPenaltyPercent);

    public void ApplyRawFoodDiscomfort(float duration, float penaltyPercent)
    {
        rawFoodDiscomfortRemaining = Mathf.Max(
            rawFoodDiscomfortRemaining,
            Mathf.Max(0f, duration));
        workPenaltyPercent = Mathf.Clamp(penaltyPercent, 0f, 0.95f);
    }

    public void ApplyTick(float elapsedSeconds)
    {
        rawFoodDiscomfortRemaining = Mathf.Max(
            0f,
            rawFoodDiscomfortRemaining - Mathf.Max(0f, elapsedSeconds));
    }

    public void Restore(float remainingDuration, float penaltyPercent)
    {
        rawFoodDiscomfortRemaining = Mathf.Max(0f, remainingDuration);
        workPenaltyPercent = Mathf.Clamp(penaltyPercent, 0f, 0.95f);
    }
}
