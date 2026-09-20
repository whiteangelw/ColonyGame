using System.Collections.Generic;
using UnityEngine;

public static class CreatureRegistry
{
    private static readonly List<CreatureController> creatures = new List<CreatureController>();
    public static IReadOnlyList<CreatureController> Creatures => creatures;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => creatures.Clear();

    public static void Register(CreatureController creature)
    {
        if (creature != null && !creatures.Contains(creature)) creatures.Add(creature);
    }

    public static void Unregister(CreatureController creature) => creatures.Remove(creature);
}
