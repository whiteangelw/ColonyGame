using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    fileName = "CreatureGardeningProfile",
    menuName = "Creatures/Abilities/Territorial Gardening")]
public sealed class CreatureGardeningProfileSO : ScriptableObject
{
    [Tooltip("Aceleração temporária aplicada após uma visita concluída.")]
    [Range(0f, 100f)] public float growthSpeedBonusPercent = 10f;

    [Tooltip("Raio de serviço medido a partir do centro do habitat.")]
    [Min(1)] public int interactionRadius = 8;

    [Tooltip("Distância permitida para cuidar da plantadeira ao chegar.")]
    [Min(1)] public int interactionDistance = 1;

    [Tooltip("Tempo que a criatura permanece cuidando antes de aplicar o efeito.")]
    [Min(0.1f)] public float tendingDuration = 2f;

    [Tooltip("Duração do bônus aplicado à plantadeira.")]
    [Min(0.1f)] public float bonusDuration = 30f;

    [Tooltip("Intervalo interno do efeito temporário; não é um Update por frame.")]
    [FormerlySerializedAs("pulseInterval")]
    [Min(0.25f)] public float effectPulseInterval = 1f;

    [Tooltip("Máximo de plantas cuidadas antes de voltar ao patrulhamento.")]
    [Min(1)] public int maximumVisitsPerRound = 4;

    [Tooltip("Pausa entre rodadas de jardinagem.")]
    [Min(0.1f)] public float gardeningRoundCooldown = 20f;

    [Tooltip("Tempo para ignorar uma plantadeira que ficou inalcançável.")]
    [Min(0.1f)] public float failedTargetCooldown = 10f;

    private void OnValidate()
    {
        growthSpeedBonusPercent = Mathf.Clamp(
            growthSpeedBonusPercent, 0f, 100f);
        interactionRadius = Mathf.Max(1, interactionRadius);
        interactionDistance = Mathf.Max(1, interactionDistance);
        tendingDuration = Mathf.Max(0.1f, tendingDuration);
        bonusDuration = Mathf.Max(0.1f, bonusDuration);
        effectPulseInterval = Mathf.Max(0.25f, effectPulseInterval);
        maximumVisitsPerRound = Mathf.Max(1, maximumVisitsPerRound);
        gardeningRoundCooldown = Mathf.Max(0.1f, gardeningRoundCooldown);
        failedTargetCooldown = Mathf.Max(0.1f, failedTargetCooldown);
    }
}
