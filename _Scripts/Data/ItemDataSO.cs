using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Colony/Data/Item Data")]
public class ItemDataSO : ScriptableObject
{
    public ResourceType resourceType;
    public string itemName;
    public Sprite icon;
    public GameObject worldPrefab; // Prefab do item dropado no chão
    public int maxStackSize = 50;
}