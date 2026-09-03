using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Colony/Data/Item Database")]
public class ItemDatabaseSO : ScriptableObject
{
    [SerializeField] private List<ItemDataSO> items;
    private Dictionary<ResourceType, ItemDataSO> _dictionary;

    public void Initialize()
    {
        _dictionary = new Dictionary<ResourceType, ItemDataSO>();
        foreach (var item in items)
        {
            if (item != null && !_dictionary.ContainsKey(item.resourceType))
                _dictionary.Add(item.resourceType, item);
        }
    }

    public ItemDataSO GetItem(ResourceType type)
    {
        if (_dictionary == null) Initialize();
        _dictionary.TryGetValue(type, out var data);
        return data;
    }
}