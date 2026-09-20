using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class DirectFeedingRule
{
    public ResourceType resourceType;
    [Min(0f)] public float domesticationProgress = 5f;
}

[CreateAssetMenu(
    fileName = "CreatureTamingProfile",
    menuName = "Creatures/Taming Profile")]
public sealed class CreatureTamingProfileSO : ScriptableObject
{
    [Tooltip("Somente alimentação direta usa estas regras.")]
    public List<DirectFeedingRule> acceptedDirectFoods =
        new List<DirectFeedingRule>();

    [Header("Ritmo de alimentação direta")]
    [Min(1f)] public float feedingWindowDuration = 120f;
    [Min(1)] public int healthyFeedingsPerWindow = 2;
    [Min(0)] public int toleratedExtraFeedings = 2;
    [Min(0f)] public float overfeedingProgressPenalty = 3f;
    [Min(0.1f)] public float overfedBehaviourDuration = 12f;
    [Min(0.1f)] public float refusalDuration = 30f;

    [Header("Satisfação social e negligência")]
    [Min(0.1f)] public float contentBehaviourDuration = 25f;
    [Min(1f)] public float neglectGraceDuration = 180f;
    [Min(1f)] public float neglectPenaltyInterval = 60f;
    [Min(0f)] public float neglectProgressPenalty = 1f;
    [Min(0.1f)] public float neglectSignalDuration = 8f;

    public int MaximumAcceptedFeedingsPerWindow =>
        Mathf.Max(1, healthyFeedingsPerWindow)
        + Mathf.Max(0, toleratedExtraFeedings);

    public bool TryGetRule(ResourceType type, out DirectFeedingRule rule)
    {
        for (int i = 0; i < acceptedDirectFoods.Count; i++)
        {
            DirectFeedingRule candidate = acceptedDirectFoods[i];
            if (candidate != null && candidate.resourceType == type)
            {
                rule = candidate;
                return true;
            }
        }

        rule = null;
        return false;
    }

    private void OnValidate()
    {
        healthyFeedingsPerWindow = Mathf.Max(1, healthyFeedingsPerWindow);
        toleratedExtraFeedings = Mathf.Max(0, toleratedExtraFeedings);
        feedingWindowDuration = Mathf.Max(1f, feedingWindowDuration);
        refusalDuration = Mathf.Max(0.1f, refusalDuration);
        neglectPenaltyInterval = Mathf.Max(1f, neglectPenaltyInterval);
    }
}
