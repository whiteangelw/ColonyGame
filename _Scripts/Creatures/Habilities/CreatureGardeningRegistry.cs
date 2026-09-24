using System.Collections.Generic;

// Reserva transitória para impedir duas criaturas de escolherem a mesma
// plantadeira durante o deslocamento.
public static class CreatureGardeningRegistry
{
    private sealed class Reservation
    {
        public CreatureTerritorialGardener owner;
        public float expiresAt;
    }

    private static readonly Dictionary<PlanterStructureBehaviour, Reservation>
        reservations = new Dictionary<PlanterStructureBehaviour, Reservation>();

    public static bool TryReserve(
        PlanterStructureBehaviour planter,
        CreatureTerritorialGardener owner,
        float duration)
    {
        Cleanup();
        if (planter == null || owner == null) return false;
        if (reservations.TryGetValue(planter, out Reservation existing)
            && existing.owner != owner)
            return false;

        reservations[planter] = new Reservation
        {
            owner = owner,
            expiresAt = UnityEngine.Time.time
                + UnityEngine.Mathf.Max(1f, duration)
        };
        return true;
    }

    public static bool IsReservedByOther(
        PlanterStructureBehaviour planter,
        CreatureTerritorialGardener owner)
    {
        Cleanup();
        return planter != null
            && reservations.TryGetValue(planter, out Reservation reservation)
            && reservation.owner != owner;
    }

    public static void Release(
        PlanterStructureBehaviour planter,
        CreatureTerritorialGardener owner)
    {
        if (planter == null || owner == null) return;
        if (reservations.TryGetValue(planter, out Reservation reservation)
            && reservation.owner == owner)
            reservations.Remove(planter);
    }

    public static void ReleaseAll(CreatureTerritorialGardener owner)
    {
        if (owner == null) return;
        List<PlanterStructureBehaviour> remove =
            new List<PlanterStructureBehaviour>();
        foreach (KeyValuePair<PlanterStructureBehaviour, Reservation> pair
            in reservations)
        {
            if (pair.Key == null || pair.Value == null
                || pair.Value.owner == null || pair.Value.owner == owner)
                remove.Add(pair.Key);
        }

        for (int i = 0; i < remove.Count; i++)
            reservations.Remove(remove[i]);
    }

    private static void Cleanup()
    {
        List<PlanterStructureBehaviour> remove =
            new List<PlanterStructureBehaviour>();
        foreach (KeyValuePair<PlanterStructureBehaviour, Reservation> pair
            in reservations)
        {
            if (pair.Key == null || pair.Value == null
                || pair.Value.owner == null
                || UnityEngine.Time.time >= pair.Value.expiresAt)
                remove.Add(pair.Key);
        }

        for (int i = 0; i < remove.Count; i++)
            reservations.Remove(remove[i]);
    }
}
