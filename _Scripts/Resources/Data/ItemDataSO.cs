using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Colony/Data/Item Data")]
public class ItemDataSO : ScriptableObject
{
    public ResourceType resourceType;
    public string itemName;
    public Sprite icon;
    public GameObject worldPrefab; // Prefab do item dropado no chão
    public int maxStackSize = 50;

    [Header("Alimentação")]
    public bool isFood;
    [Min(0f)] public float hungerRestored = 25f;
    public bool isRawFood;
    [Range(0, 5)] public int foodQuality = 1;
    [Tooltip("Se desmarcado, só será considerado em fome crítica.")]
    public bool allowPreventiveConsumption = true;
}
