using System.Collections.Generic;
using UnityEngine;

public class FoodSourceRegistry : MonoBehaviour
{
    public static FoodSourceRegistry Instance { get; private set; }

    [SerializeField, Min(1)] private int maximumPathChecksPerSearch = 12;
    private readonly List<IFoodSource> sources = new List<IFoodSource>();

    public int AvailableSourceCount => sources.Count;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        MonoBehaviour[] existing = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in existing)
        {
            if (behaviour is IFoodSource source) Register(source);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(IFoodSource source)
    {
        if (source != null && !sources.Contains(source)) sources.Add(source);
    }

    public void Unregister(IFoodSource source)
    {
        if (source != null) sources.Remove(source);
    }

    public bool TryReserveReachableFood(
        DuplicantController duplicant,
        bool allowEmergencySources,
        out IFoodSource selectedSource,
        out List<Vector2Int> path)
    {
        selectedSource = null;
        path = null;
        if (duplicant == null) return false;

        sources.RemoveAll(source => !IsValid(source) || !source.HasFood);
        sources.Sort((a, b) => CompareSources(
            duplicant.gridPosition,
            a,
            b));

        int checks = Mathf.Min(maximumPathChecksPerSearch, sources.Count);
        for (int i = 0; i < checks; i++)
        {
            IFoodSource source = sources[i];
            if (!allowEmergencySources && source.IsEmergencyOnly) continue;
            List<Vector2Int> candidatePath =
                TaskNavigationUtility.GetPathToInteractionPosition(
                    duplicant.gridPosition,
                    source.GridPosition,
                    true,
                    duplicant.capabilityProfile);

            if (candidatePath == null || !source.TryReservePortion(duplicant))
            {
                continue;
            }

            selectedSource = source;
            path = candidatePath;
            return true;
        }

        return false;
    }

    private static int CompareSources(
        Vector2Int origin,
        IFoodSource a,
        IFoodSource b)
    {
        int sourceType = a.IsEmergencyOnly.CompareTo(b.IsEmergencyOnly);
        return sourceType != 0
            ? sourceType
            : SquaredDistance(origin, a.GridPosition).CompareTo(
                SquaredDistance(origin, b.GridPosition));
    }

    private static bool IsValid(IFoodSource source)
    {
        return source != null
            && (!(source is Object unityObject) || unityObject != null);
    }

    private static int SquaredDistance(Vector2Int a, Vector2Int b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        return dx * dx + dy * dy;
    }
}
