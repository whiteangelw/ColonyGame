using System;
using System.Collections.Generic;
using UnityEngine;

public class ReachabilityManager : Singleton<ReachabilityManager>
{
    private static readonly HashSet<Vector2Int> EmptyReachableSet =
        new HashSet<Vector2Int>();

    [Header("Performance")]
    [SerializeField, Min(0.05f)]
    private float minimumRecalculationInterval = 0.25f;

    [SerializeField, Min(8)]
    private int maximumCachedOrigins = 128;

    private NavGraphGenerator navGraph;

    private readonly Dictionary<ReachabilityCacheKey, HashSet<Vector2Int>>
        reachablePositionsCache =
            new Dictionary<ReachabilityCacheKey, HashSet<Vector2Int>>();

    private readonly HashSet<DuplicantController> registeredDuplicants =
        new HashSet<DuplicantController>();

    private readonly Queue<NavNode> traversalQueue =
        new Queue<NavNode>();

    public bool IsReady { get; private set; }

    private GridManager subscribedGridManager;
    private bool recalculationPending;
    private float nextAllowedRecalculationTime;

    private void Start()
    {
        navGraph = NavGraphGenerator.Instance;
        RegisterExistingDuplicants();

        subscribedGridManager = GridManager.Instance;

        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnGridRebuilt += RequestRecalculation;
            subscribedGridManager.OnTileChanged += OnTileChanged;

            if (subscribedGridManager.IsGridReady)
            {
                RequestRecalculation();
            }
        }
    }

    private void LateUpdate()
    {
        if (SaveGameRuntime.IsLoading
            || !recalculationPending
            || Time.unscaledTime < nextAllowedRecalculationTime)
        {
            return;
        }

        if (navGraph == null)
        {
            navGraph = NavGraphGenerator.Instance;
        }

        // Aguarda o NavGraph processar as regiões alteradas antes de
        // reconstruir o cache de alcance.
        if (navGraph != null && navGraph.HasPendingNavigationUpdates)
        {
            return;
        }

        recalculationPending = false;
        RecalculateAllGroups();
    }

    private void OnDestroy()
    {
        if (subscribedGridManager == null)
        {
            return;
        }

        subscribedGridManager.OnGridRebuilt -= RequestRecalculation;
        subscribedGridManager.OnTileChanged -= OnTileChanged;
    }

    private void OnTileChanged(int x, int y, TileType type)
    {
        RequestRecalculation();
    }

    public void RequestRecalculation()
    {
        recalculationPending = true;
    }

    public void RecalculateAllGroups()
    {
        if (navGraph == null)
        {
            navGraph = NavGraphGenerator.Instance;
        }

        if (navGraph == null
            || !navGraph.IsGraphReady
            || GridManager.Instance == null)
        {
            RequestRecalculation();
            return;
        }

        IsReady = false;
        reachablePositionsCache.Clear();

        IsReady = true;

        nextAllowedRecalculationTime =
            Time.unscaledTime + minimumRecalculationInterval;

        StockpileManager.Instance?.RequestRefresh();
        StructureManager.Instance?.QueueExistingGroundItems();
    }

    public bool CanAnyDuplicantReach(Vector2Int targetPosition)
    {
        if (!IsReady)
        {
            return false;
        }

        foreach (DuplicantController duplicant in registeredDuplicants)
        {
            if (duplicant != null
                && CanReach(
                    duplicant.gridPosition,
                    targetPosition,
                    duplicant.capabilityProfile))
            {
                return true;
            }
        }

        return false;
    }

    public void RegisterDuplicant(DuplicantController duplicant)
    {
        if (duplicant != null)
        {
            registeredDuplicants.Add(duplicant);
        }
    }

    public void UnregisterDuplicant(DuplicantController duplicant)
    {
        if (!ReferenceEquals(duplicant, null))
        {
            registeredDuplicants.Remove(duplicant);
        }
    }

    private void RegisterExistingDuplicants()
    {
        DuplicantController[] duplicants =
            UnityEngine.Object.FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (DuplicantController duplicant in duplicants)
        {
            RegisterDuplicant(duplicant);
        }
    }

    public bool CanReachExact(
        Vector2Int dupePos,
        Vector2Int targetPosition,
        DuplicantCapabilityProfile profile = null)
    {
        if (!IsReady)
        {
            return false;
        }

        HashSet<Vector2Int> reachable = GetReachablePositions(
            dupePos,
            profile);

        return reachable.Contains(targetPosition);
    }

    /// <summary>
    /// Verifica se existe uma posição de interação alcançável pelo colono.
    /// </summary>
    public bool CanReach(
        Vector2Int dupePos,
        Vector2Int taskTargetPos,
        DuplicantCapabilityProfile profile = null)
    {
        if (!IsReady || navGraph == null)
        {
            return false;
        }

        DuplicantCapabilityProfile effectiveProfile =
            profile != null ? profile : navGraph.DefaultProfile;

        if (effectiveProfile == null)
        {
            return false;
        }

        HashSet<Vector2Int> reachable = GetReachablePositions(
            dupePos,
            effectiveProfile);

        int interactionRange = Mathf.Max(
            1,
            effectiveProfile.buildAndDigRange);

        int horizontalRange = Mathf.Min(2, interactionRange);
        int maximumVerticalOffset = interactionRange - 1;

        for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
        {
            for (int dy = -1; dy <= maximumVerticalOffset; dy++)
            {
                Vector2Int standCandidate =
                    taskTargetPos - new Vector2Int(dx, dy);

                if (reachable.Contains(standCandidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private HashSet<Vector2Int> GetReachablePositions(
        Vector2Int startPosition,
        DuplicantCapabilityProfile profile)
    {
        DuplicantCapabilityProfile effectiveProfile =
            profile != null ? profile : navGraph.DefaultProfile;

        if (effectiveProfile == null)
        {
            return EmptyReachableSet;
        }

        ReachabilityCacheKey key = new ReachabilityCacheKey(
            startPosition,
            effectiveProfile);

        if (reachablePositionsCache.TryGetValue(
                key,
                out HashSet<Vector2Int> cached))
        {
            return cached;
        }

        if (reachablePositionsCache.Count >= maximumCachedOrigins)
        {
            reachablePositionsCache.Clear();
        }

        HashSet<Vector2Int> reachable = BuildReachableSet(
            startPosition,
            effectiveProfile);

        reachablePositionsCache[key] = reachable;

        return reachable;
    }

    private HashSet<Vector2Int> BuildReachableSet(
        Vector2Int startPosition,
        DuplicantCapabilityProfile profile)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        NavNode startNode = navGraph.GetNode(startPosition);

        if (startNode == null
            || !navGraph.IsNavigablePosition(
                startPosition.x,
                startPosition.y))
        {
            return visited;
        }

        traversalQueue.Clear();
        traversalQueue.Enqueue(startNode);
        visited.Add(startPosition);

        while (traversalQueue.Count > 0)
        {
            NavNode current = traversalQueue.Dequeue();

            foreach (NavEdge edge in current.connections)
            {
                if (edge.targetNode == null
                    || !navGraph.CanProfileUseEdge(
                        profile,
                        current.gridPosition,
                        edge))
                {
                    continue;
                }

                Vector2Int target = edge.targetNode.gridPosition;

                if (!visited.Add(target))
                {
                    continue;
                }

                traversalQueue.Enqueue(edge.targetNode);
            }
        }

        return visited;
    }

    private readonly struct ReachabilityCacheKey
        : IEquatable<ReachabilityCacheKey>
    {
        private readonly Vector2Int startPosition;
        private readonly DuplicantCapabilityProfile profile;

        public ReachabilityCacheKey(
            Vector2Int startPosition,
            DuplicantCapabilityProfile profile)
        {
            this.startPosition = startPosition;
            this.profile = profile;
        }

        public bool Equals(ReachabilityCacheKey other)
        {
            return startPosition == other.startPosition
                && ReferenceEquals(profile, other.profile);
        }

        public override bool Equals(object obj)
        {
            return obj is ReachabilityCacheKey other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int profileHash = profile != null
                    ? System.Runtime.CompilerServices.RuntimeHelpers
                        .GetHashCode(profile)
                    : 0;

                return (startPosition.GetHashCode() * 397) ^ profileHash;
            }
        }
    }
}