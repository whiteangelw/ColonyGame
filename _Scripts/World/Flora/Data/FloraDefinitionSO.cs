using System;
using System.Collections.Generic;
using UnityEngine;

public enum FloraActionType
{
    Harvest = 0,
    Chop = 1
}

[Serializable]
public sealed class FloraYieldEntry
{
    public ResourceType resourceType;
    [Range(0f, 1f)] public float chance = 1f;
    [Min(0)] public int minimumAmount = 1;
    [Min(0)] public int maximumAmount = 1;

    public int Roll()
    {
        if (chance <= 0f || maximumAmount <= 0
            || UnityEngine.Random.value > chance)
        {
            return 0;
        }

        int minimum = Mathf.Clamp(minimumAmount, 0, maximumAmount);
        return UnityEngine.Random.Range(minimum, maximumAmount + 1);
    }
}

[Serializable]
public sealed class PlantingRequirement
{
    public ResourceType resourceType;
    [Min(1)] public int amount = 1;
}

[CreateAssetMenu(fileName = "NewFloraDefinition", menuName = "Colony/Flora/Definition")]
public class FloraDefinitionSO : ScriptableObject
{
    [Header("Identidade")]
    public string floraId = "wild_berry_bush";
    public GameObject prefab;

    [Header("Comportamento")]
    public bool providesFood = true;
    [Tooltip("Ação criada quando esta flora é marcada com a ferramenta H.")]
    public FloraActionType actionType = FloraActionType.Harvest;

    [Header("Alimento")]
    public ResourceType foodResourceType = ResourceType.WildBerry;
    [Min(1)] public int initialPortions = 3;
    [Min(0.1f)] public float hungerRestoredPerPortion = 35f;
    public bool isRawFood = true;
    [Range(0, 5)] public int foodQuality;

    [Header("Apresentação no cultivo (opcional)")]
    [Tooltip("Tempo desta espécie na plantadeira. Zero usa o valor da PlanterSettingsSO.")]
    [Min(0f)] public float planterGrowthDuration;
    [Tooltip("Sprite durante crescimento. Vazio usa o fallback da plantadeira.")]
    public Sprite planterGrowingSprite;
    [Tooltip("Sprite pronto. Vazio tenta usar o SpriteRenderer do prefab.")]
    public Sprite planterReadySprite;

    [Header("Plantio")]
    [Tooltip("Recursos entregues à plantadeira antes do crescimento começar.")]
    public List<PlantingRequirement> plantingRequirements =
        new List<PlantingRequirement>();

    [Header("Colheita")]
    [Tooltip("Quantidade retirada por ordem de colheita.")]
    [Min(1)] public int harvestUnitsPerAction = 3;

    [Tooltip("Trabalho base necessário. Um colono com velocidade 1 leva este valor em segundos.")]
    [Min(0.1f)] public float harvestWorkRequired = 2f;

    [Tooltip("Mantém o progresso quando o trabalhador é interrompido.")]
    public bool preserveWorkOnInterruption;

    [Tooltip("Se preenchido, substitui o drop legado de alimento.")]
    public List<FloraYieldEntry> yields = new List<FloraYieldEntry>();

    public float HarvestWorkRequired => Mathf.Max(0.1f, harvestWorkRequired);
    public bool HasConfiguredYields => yields != null && yields.Count > 0;

    private void OnValidate()
    {
        initialPortions = Mathf.Max(1, initialPortions);
        harvestUnitsPerAction = Mathf.Max(1, harvestUnitsPerAction);
        harvestWorkRequired = Mathf.Max(0.1f, harvestWorkRequired);
        planterGrowthDuration = Mathf.Max(0f, planterGrowthDuration);

        if (plantingRequirements != null)
        {
            foreach (PlantingRequirement requirement in plantingRequirements)
            {
                if (requirement != null)
                {
                    requirement.amount = Mathf.Max(1, requirement.amount);
                }
            }
        }

        if (yields == null) return;
        foreach (FloraYieldEntry entry in yields)
        {
            if (entry == null) continue;
            entry.minimumAmount = Mathf.Max(0, entry.minimumAmount);
            entry.maximumAmount = Mathf.Max(entry.minimumAmount, entry.maximumAmount);
        }
    }
}
