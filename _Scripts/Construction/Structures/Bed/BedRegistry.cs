using System.Collections.Generic;
using UnityEngine;

public static class BedRegistry
{
    private static readonly List<BedStructureBehaviour> beds =
        new List<BedStructureBehaviour>();
    private static readonly List<BedStructureBehaviour> candidates =
        new List<BedStructureBehaviour>();

    public static int RegisteredCount => beds.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        beds.Clear();
        candidates.Clear();
    }

    public static void Register(BedStructureBehaviour bed)
    {
        if (bed != null && !beds.Contains(bed)) beds.Add(bed);
    }

    public static void Unregister(BedStructureBehaviour bed)
    {
        if (bed != null) beds.Remove(bed);
    }

    public static bool TryReserveBest(
        DuplicantController duplicant,
        LifeCycleSettingsSO settings,
        out BedStructureBehaviour selectedBed,
        out List<Vector2Int> selectedPath)
    {
        selectedBed = null;
        selectedPath = null;
        if (duplicant == null) return false;

        int searchRadius = settings != null
            ? Mathf.Max(1, settings.bedSearchRadius)
            : 80;
        int radiusSquared = searchRadius * searchRadius;
        int maximumChecks = settings != null
            ? Mathf.Max(1, settings.maximumBedPathChecks)
            : 8;

        candidates.Clear();
        for (int i = beds.Count - 1; i >= 0; i--)
        {
            BedStructureBehaviour bed = beds[i];
            if (bed == null)
            {
                beds.RemoveAt(i);
                continue;
            }

            if (!bed.IsAvailableFor(duplicant)) continue;
            Vector2Int offset = bed.GridPosition - duplicant.gridPosition;
            if (offset.sqrMagnitude <= radiusSquared) candidates.Add(bed);
        }

        candidates.Sort((a, b) =>
            ((a.GridPosition - duplicant.gridPosition).sqrMagnitude)
            .CompareTo((b.GridPosition - duplicant.gridPosition).sqrMagnitude));

        int checkedPaths = 0;
        for (int i = 0; i < candidates.Count && checkedPaths < maximumChecks; i++)
        {
            BedStructureBehaviour bed = candidates[i];
            checkedPaths++;
            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToInteractionPosition(
                    duplicant.gridPosition,
                    bed.GridPosition,
                    true,
                    duplicant.capabilityProfile);

            if (path == null || !bed.TryReserve(duplicant)) continue;
            selectedBed = bed;
            selectedPath = path;
            return true;
        }

        return false;
    }
}
