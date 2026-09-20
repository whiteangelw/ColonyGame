using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CreatureController))]
public sealed class CreaturePerception : MonoBehaviour
{
    private readonly List<ResourceItem> itemBuffer = new List<ResourceItem>(64);
    private CreatureController controller;

    private void Awake() => controller = GetComponent<CreatureController>();

    public bool TryFindClosestResource(IReadOnlyList<ResourceType> accepted,
        int radius, out ResourceItem closest)
    {
        closest = null;
        if (accepted == null || accepted.Count == 0) return false;

        ResourceItem.CopyActiveItemsTo(itemBuffer);
        int bestDistance = int.MaxValue;
        Vector2Int origin = controller.GridPosition;

        for (int i = 0; i < itemBuffer.Count; i++)
        {
            ResourceItem item = itemBuffer[i];
            if (item == null || item.amount <= 0 || !Contains(accepted, item.type)) continue;
            int distance = Mathf.Abs(item.GridPosition.x - origin.x)
                + Mathf.Abs(item.GridPosition.y - origin.y);
            if (distance <= radius && distance < bestDistance)
            {
                bestDistance = distance;
                closest = item;
            }
        }

        return closest != null;
    }

    public bool TryFindClosestAvailableFood(
        IReadOnlyList<ResourceType> accepted,
        int radius,
        CreatureFeeding requester,
        out ResourceItem closest)
    {
        closest = null;
        if (accepted == null || accepted.Count == 0 || requester == null) return false;

        ResourceItem.CopyActiveItemsTo(itemBuffer);
        int bestDistance = int.MaxValue;
        Vector2Int origin = controller.GridPosition;

        for (int i = 0; i < itemBuffer.Count; i++)
        {
            ResourceItem item = itemBuffer[i];
            if (item == null || !Contains(accepted, item.type)
                || !item.CanReserveCreaturePortion(requester, item.type)) continue;

            int distance = Mathf.Abs(item.GridPosition.x - origin.x)
                + Mathf.Abs(item.GridPosition.y - origin.y);
            if (distance <= radius && distance < bestDistance)
            {
                bestDistance = distance;
                closest = item;
            }
        }

        return closest != null;
    }

    private static bool Contains(IReadOnlyList<ResourceType> list, ResourceType type)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == type) return true;
        return false;
    }
}
