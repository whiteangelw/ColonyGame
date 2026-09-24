using System.Collections.Generic;
using UnityEngine;

public static class CreatureHabitatRegistry
{
    private static readonly HashSet<CreatureHabitatBehaviour> habitats =
        new HashSet<CreatureHabitatBehaviour>();

    public static int Count
    {
        get
        {
            RemoveInvalidEntries();
            return habitats.Count;
        }
    }

    public static void Register(CreatureHabitatBehaviour habitat)
    {
        if (habitat != null) habitats.Add(habitat);
    }

    public static void Unregister(CreatureHabitatBehaviour habitat)
    {
        if (habitat != null) habitats.Remove(habitat);
    }

    public static bool TryFindNearestAvailable(
        CreatureController creature,
        out CreatureHabitatBehaviour selected)
    {
        selected = null;
        if (creature == null) return false;

        RemoveInvalidEntries();
        int bestDistance = int.MaxValue;
        foreach (CreatureHabitatBehaviour habitat in habitats)
        {
            if (!habitat.CanAccept(creature)) continue;

            int distance = Manhattan(
                creature.GridPosition,
                habitat.PatrolCenter);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            selected = habitat;
        }

        return selected != null;
    }

    public static bool TryGetAt(
        Vector2Int gridPosition,
        out CreatureHabitatBehaviour selected)
    {
        RemoveInvalidEntries();
        foreach (CreatureHabitatBehaviour habitat in habitats)
        {
            if (habitat.IsInitialized && habitat.GridPosition == gridPosition)
            {
                selected = habitat;
                return true;
            }
        }

        selected = null;
        return false;
    }

    public static void FillSnapshot(List<CreatureHabitatBehaviour> destination)
    {
        destination.Clear();
        RemoveInvalidEntries();
        foreach (CreatureHabitatBehaviour habitat in habitats)
            destination.Add(habitat);
    }

    private static void RemoveInvalidEntries()
    {
        habitats.RemoveWhere(habitat => habitat == null);
    }

    private static int Manhattan(Vector2Int left, Vector2Int right)
    {
        return Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);
    }
}
