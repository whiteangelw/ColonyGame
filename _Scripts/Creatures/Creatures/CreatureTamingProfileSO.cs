using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Cuidado por ciclo")]
    [Tooltip("Mínimo necessário no ciclo para evitar negligência.")]
    [Min(0)] public int minimumFeedingsPerCycle = 1;
    [Tooltip("Até este valor, cada alimentação é saudável.")]
    [FormerlySerializedAs("healthyFeedingsPerWindow")]
    [Min(1)] public int healthyFeedingsPerCycle = 2;
    [Tooltip("Quantidade adicional aceita com penalidade antes da recusa.")]
    [Min(0)] public int toleratedExtraFeedings = 2;
    [Min(0f)] public float overfeedingProgressPenalty = 3f;
    [Min(0.1f)] public float overfedBehaviourDuration = 12f;
    [Min(0.1f)] public float rejectedOfferingBehaviourDuration = 6f;

    [Header("Satisfação e negligência")]
    [Min(0.1f)] public float contentBehaviourDuration = 25f;
    [Tooltip("Perda por alimentação mínima ausente no fechamento do ciclo.")]
    [FormerlySerializedAs("neglectProgressPenalty")]
    [Min(0f)] public float neglectPenaltyPerMissingFeeding = 3f;
    [Min(0.1f)] public float neglectSignalDuration = 8f;

    public int MaximumAcceptedFeedingsPerCycle =>
        Mathf.Max(1, healthyFeedingsPerCycle)
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
        healthyFeedingsPerCycle = Mathf.Max(1, healthyFeedingsPerCycle);
        minimumFeedingsPerCycle = Mathf.Clamp(
            minimumFeedingsPerCycle, 0, healthyFeedingsPerCycle);
        toleratedExtraFeedings = Mathf.Max(0, toleratedExtraFeedings);
        neglectPenaltyPerMissingFeeding = Mathf.Max(
            0f, neglectPenaltyPerMissingFeeding);
    }
}
