using System.Collections.Generic;
using UnityEngine;

public interface IStorage
{
    Vector2Int GridPosition { get; }
    bool CanStoreItem(ResourceType type, int amount);
    bool TryReserveSpace(ResourceType type, int amount);
    void ReleaseReservedSpace(ResourceType type, int amount);
    bool StoreItem(ResourceType type, int amount);
    bool StoreReservedItem(ResourceType type, int amount);
    bool WithdrawItem(ResourceType type, int amount);
    Dictionary<ResourceType, int> GetAllStoredItems();
}
