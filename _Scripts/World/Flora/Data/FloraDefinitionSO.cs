using UnityEngine;

[CreateAssetMenu(fileName = "NewFloraDefinition", menuName = "Colony/Flora/Definition")]
public class FloraDefinitionSO : ScriptableObject
{
    [Header("Identidade")]
    public string floraId = "wild_berry_bush";
    public GameObject prefab;

    [Header("Comportamento")]
    public bool providesFood = true;

    [Header("Alimento")]
    public ResourceType foodResourceType = ResourceType.WildBerry;
    [Min(1)] public int initialPortions = 3;
    [Min(0.1f)] public float hungerRestoredPerPortion = 35f;
    public bool isRawFood = true;
    [Range(0, 5)] public int foodQuality;

    [Header("Colheita")]
    [Tooltip("Quantidade retirada por ordem de colheita.")]
    [Min(1)] public int harvestUnitsPerAction = 3;
}
