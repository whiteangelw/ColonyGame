using System.Collections.Generic;

public static class PlanterGrowthRegistry
{
    private static readonly HashSet<PlanterStructureBehaviour> planters =
        new HashSet<PlanterStructureBehaviour>();

    public static void Register(PlanterStructureBehaviour planter)
    {
        if (planter != null) planters.Add(planter);
    }

    public static void Unregister(PlanterStructureBehaviour planter)
    {
        if (planter != null) planters.Remove(planter);
    }

    public static void FillSnapshot(List<PlanterStructureBehaviour> destination)
    {
        destination.Clear();
        planters.RemoveWhere(planter => planter == null);
        foreach (PlanterStructureBehaviour planter in planters)
            destination.Add(planter);
    }
}
