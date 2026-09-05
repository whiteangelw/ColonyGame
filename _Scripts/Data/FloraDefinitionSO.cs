using UnityEngine;

[CreateAssetMenu(fileName = "NewFloraDefinition", menuName = "Colony/Flora/Definition")]
public class FloraDefinitionSO : ScriptableObject
{
    [Header("Identidade")]
    public string floraId = "wild_berry_bush";
    public GameObject prefab;

    [Header("Alimento")]
    [Min(1)] public int initialPortions = 3;
    [Min(0.1f)] public float hungerRestoredPerPortion = 35f;
    public bool isRawFood = true;
}
