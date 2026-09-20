using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class NavGraphGenerator : Singleton<NavGraphGenerator>
{
    private static readonly ProfilerMarker FullRebuildMarker =
        new ProfilerMarker("Colony.NavGraph.FullRebuild");
    private static readonly ProfilerMarker FullRebuildBatchMarker =
        new ProfilerMarker("Colony.NavGraph.FullRebuildBatch");
    private static readonly ProfilerMarker PartialRebuildMarker =
        new ProfilerMarker("Colony.NavGraph.PartialRebuild");
    private const int MaximumSupportedStepUp = 2;
    private const int MaximumSupportedDropHeight = 5;
    private const int MaximumSupportedJumpGap = 3;

    [Header("Referências")]
    [SerializeField] private DuplicantCapabilityProfile defaultProfile;

    [Header("Configurações de Invalidação")]
    [Tooltip("Raio horizontal recalculado quando um tile muda.")]
    [SerializeField] private int dirtyRadiusX = 4;

    [Tooltip("Raio vertical recalculado quando um tile muda.")]
    [SerializeField] private int dirtyRadiusY = 4;

    [Header("Carregamento incremental")]
    [SerializeField, Min(1)] private int fullRebuildColumnsPerFrame = 8;

    [Header("P5E - Atualização regional")]
    [Tooltip("Quantidade máxima de regiões atualizadas por frame.")]
    [SerializeField, Min(1)] private int maximumDirtyRegionsPerFrame = 2;

    private GridManager gridManager;
    private NavNode[,] nodeGrid;
    private readonly Dictionary<int, PendingNavigationRegion>
        pendingNavigationRegions =
            new Dictionary<int, PendingNavigationRegion>(64);
    private readonly Queue<int> pendingNavigationOrder = new Queue<int>(64);
    private readonly Stack<PendingNavigationRegion> pendingRegionPool =
        new Stack<PendingNavigationRegion>(64);

    private int width;
    private int height;

    public bool IsGraphReady { get; private set; }
    public DuplicantCapabilityProfile DefaultProfile => defaultProfile;
    public int PendingNavigationRegionCount => pendingNavigationRegions.Count;
    public bool HasPendingNavigationUpdates =>
        pendingNavigationRegions.Count > 0;

    private sealed class PendingNavigationRegion
    {
        public int MinimumX { get; private set; }
        public int MaximumX { get; private set; }
        public int MinimumY { get; private set; }
        public int MaximumY { get; private set; }

        public PendingNavigationRegion(
            int minimumX,
            int maximumX,
            int minimumY,
            int maximumY)
        {
            Reset(minimumX, maximumX, minimumY, maximumY);
        }

        public void Reset(
            int minimumX,
            int maximumX,
            int minimumY,
            int maximumY)
        {
            MinimumX = minimumX;
            MaximumX = maximumX;
            MinimumY = minimumY;
            MaximumY = maximumY;
        }

        public void Include(
            int minimumX,
            int maximumX,
            int minimumY,
            int maximumY)
        {
            MinimumX = Mathf.Min(MinimumX, minimumX);
            MaximumX = Mathf.Max(MaximumX, maximumX);
            MinimumY = Mathf.Min(MinimumY, minimumY);
            MaximumY = Mathf.Max(MaximumY, maximumY);
        }
    }

    private void Start()
    {
        gridManager = GridManager.Instance;

        if (gridManager == null)
        {
            Debug.LogError(
                "[NavGraphGenerator] GridManager não encontrado."
            );

            return;
        }

        gridManager.OnGridRebuilt += HandleGridRebuilt;
        gridManager.OnTileChanged += OnTileChangedHandler;

        if (gridManager.IsGridReady)
        {
            RegenerateGraph();
        }
    }

    private void OnDestroy()
    {
        if (gridManager == null)
        {
            ClearPendingNavigationUpdates();
            return;
        }

        gridManager.OnGridRebuilt -= HandleGridRebuilt;
        gridManager.OnTileChanged -= OnTileChangedHandler;
        ClearPendingNavigationUpdates();
    }

    private void LateUpdate()
    {
        ProcessPendingNavigationUpdates(
            Mathf.Max(1, maximumDirtyRegionsPerFrame),
            false);
    }

    private void HandleGridRebuilt()
    {
        if (!SaveGameRuntime.IsLoading)
        {
            RegenerateGraph();
        }
    }

    public IEnumerator RegenerateGraphIncrementally()
    {
        if (gridManager == null || !gridManager.IsGridReady)
        {
            yield break;
        }

        if (defaultProfile == null)
        {
            IsGraphReady = false;
            throw new System.InvalidOperationException(
                "NavGraphGenerator Default Profile não foi configurado.");
        }

        IsGraphReady = false;
        ClearPendingNavigationUpdates();
        width = gridManager.width;
        height = gridManager.height;
        nodeGrid = new NavNode[width, height];
        int columnsPerFrame = Mathf.Max(1, fullRebuildColumnsPerFrame);

        for (int startX = 0; startX < width; startX += columnsPerFrame)
        {
            int endX = Mathf.Min(startX + columnsPerFrame, width);
            using (FullRebuildBatchMarker.Auto())
            {
                for (int x = startX; x < endX; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        nodeGrid[x, y] = new NavNode(x, y);
                    }
                }
            }

            if (endX < width)
            {
                yield return null;
            }
        }

        for (int startX = 0; startX < width; startX += columnsPerFrame)
        {
            int endX = Mathf.Min(startX + columnsPerFrame, width);
            using (FullRebuildBatchMarker.Auto())
            {
                for (int x = startX; x < endX; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (IsNavigablePosition(x, y))
                        {
                            CalculateNodeEdges(x, y);
                        }
                    }
                }
            }

            if (endX < width)
            {
                yield return null;
            }
        }

        PerformanceMetricsService.RecordNavRegeneration(true, width * height);
        IsGraphReady = true;
    }

    private void OnTileChangedHandler(
        int x,
        int y,
        TileType newType)
    {
        if (!IsGraphReady)
        {
            return;
        }

        QueueDirtyRegion(x, y);
    }

    public NavNode GetNode(int x, int y)
    {
        if (!IsInsideGrid(x, y))
        {
            return null;
        }

        return nodeGrid[x, y];
    }

    public NavNode GetNode(Vector2Int position)
    {
        return GetNode(position.x, position.y);
    }

    public bool CanTraverse(
        Vector2Int from,
        Vector2Int to,
        INavigationProfile profile = null)
    {
        FlushPendingNavigationUpdates();

        if (!IsGraphReady)
        {
            return false;
        }

        if (from == to)
        {
            return IsNavigablePosition(from.x, from.y);
        }

        NavNode originNode = GetNode(from);
        NavNode targetNode = GetNode(to);

        if (originNode == null || targetNode == null)
        {
            return false;
        }

        foreach (NavEdge edge in originNode.connections)
        {
            if (edge.targetNode == targetNode
                && CanProfileUseEdge(profile, from, edge))
            {
                return true;
            }
        }

        return false;
    }

    [ContextMenu("Regenerate Graph")]
    public void RegenerateGraph()
    {
        if (gridManager == null || !gridManager.IsGridReady)
        {
            return;
        }

        if (defaultProfile == null)
        {
            Debug.LogError(
                "[NavGraphGenerator] Default Profile não foi configurado."
            );

            IsGraphReady = false;
            return;
        }

        ClearPendingNavigationUpdates();
        width = gridManager.width;
        height = gridManager.height;

        long startedAt = PerformanceMetricsService.BeginSample();
        using (FullRebuildMarker.Auto())
        {
            nodeGrid = new NavNode[width, height];
            CreateNodes();
            CreateConnections();
        }

        PerformanceMetricsService.RecordNavRegeneration(true, width * height);
        PerformanceMetricsService.EndSample(
            PerformanceMetric.NavGraphFull,
            startedAt);

        IsGraphReady = true;
    }

    private void CreateNodes()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                nodeGrid[x, y] = new NavNode(x, y);
            }
        }
    }

    private void CreateConnections()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (IsNavigablePosition(x, y))
                {
                    CalculateNodeEdges(x, y);
                }
            }
        }
    }

    public void UpdateDirtyRegion(int centerX, int centerY)
    {
        if (nodeGrid == null)
        {
            return;
        }

        CalculateDirtyBounds(
            centerX,
            centerY,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY);

        RebuildNavigationBounds(minX, maxX, minY, maxY);
    }

    public void FlushPendingNavigationUpdates()
    {
        ProcessPendingNavigationUpdates(int.MaxValue, true);
    }

    private void QueueDirtyRegion(int centerX, int centerY)
    {
        if (nodeGrid == null || gridManager == null)
        {
            return;
        }

        if (!gridManager.Regions.TryGetByCell(
                centerX,
                centerY,
                out WorldRegionState region))
        {
            UpdateDirtyRegion(centerX, centerY);
            return;
        }

        CalculateDirtyBounds(
            centerX,
            centerY,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY);

        int regionKey = region.Coordinate.X
            + region.Coordinate.Y * gridManager.RegionColumns;

        if (pendingNavigationRegions.TryGetValue(
                regionKey,
                out PendingNavigationRegion pendingRegion))
        {
            pendingRegion.Include(minX, maxX, minY, maxY);
        }
        else
        {
            pendingNavigationRegions.Add(
                regionKey,
                RentPendingRegion(minX, maxX, minY, maxY));
            pendingNavigationOrder.Enqueue(regionKey);
        }

        PerformanceMetricsService.RecordNavRegionalQueue(
            pendingNavigationRegions.Count,
            false);
    }

    private void ProcessPendingNavigationUpdates(
        int maximumRegions,
        bool forcedFlush)
    {
        if (!IsGraphReady
            || nodeGrid == null
            || pendingNavigationOrder.Count == 0)
        {
            return;
        }

        int processedRegions = 0;

        while (processedRegions < maximumRegions
               && pendingNavigationOrder.Count > 0)
        {
            int regionKey = pendingNavigationOrder.Dequeue();

            if (!pendingNavigationRegions.TryGetValue(
                    regionKey,
                    out PendingNavigationRegion pendingRegion))
            {
                continue;
            }

            pendingNavigationRegions.Remove(regionKey);

            RebuildNavigationBounds(
                pendingRegion.MinimumX,
                pendingRegion.MaximumX,
                pendingRegion.MinimumY,
                pendingRegion.MaximumY);

            ReturnPendingRegion(pendingRegion);
            processedRegions++;
        }

        PerformanceMetricsService.RecordNavRegionalQueue(
            pendingNavigationRegions.Count,
            forcedFlush && processedRegions > 0);
    }

    private void CalculateDirtyBounds(
        int centerX,
        int centerY,
        out int minX,
        out int maxX,
        out int minY,
        out int maxY)
    {
        int effectiveRadiusX = Mathf.Max(
            dirtyRadiusX,
            MaximumSupportedJumpGap + 1);

        int effectiveRadiusY = Mathf.Max(
            dirtyRadiusY,
            MaximumSupportedDropHeight + 1);

        minX = Mathf.Clamp(centerX - effectiveRadiusX, 0, width - 1);
        maxX = Mathf.Clamp(centerX + effectiveRadiusX, 0, width - 1);
        minY = Mathf.Clamp(centerY - effectiveRadiusY, 0, height - 1);
        maxY = Mathf.Clamp(centerY + effectiveRadiusY, 0, height - 1);
    }

    private void RebuildNavigationBounds(
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        long startedAt = PerformanceMetricsService.BeginSample();

        using (PartialRebuildMarker.Auto())
        {
            ClearDirtyRegion(minX, maxX, minY, maxY);
            RebuildDirtyRegion(minX, maxX, minY, maxY);
        }

        int regeneratedCells =
            (maxX - minX + 1) * (maxY - minY + 1);

        PerformanceMetricsService.RecordNavRegeneration(
            false,
            regeneratedCells);

        PerformanceMetricsService.EndSample(
            PerformanceMetric.NavGraphPartial,
            startedAt);
    }

    private void ClearPendingNavigationUpdates()
    {
        foreach (PendingNavigationRegion pendingRegion
                 in pendingNavigationRegions.Values)
        {
            ReturnPendingRegion(pendingRegion);
        }

        pendingNavigationRegions.Clear();
        pendingNavigationOrder.Clear();
    }

    private PendingNavigationRegion RentPendingRegion(
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        if (pendingRegionPool.Count == 0)
        {
            return new PendingNavigationRegion(
                minX,
                maxX,
                minY,
                maxY);
        }

        PendingNavigationRegion pendingRegion = pendingRegionPool.Pop();
        pendingRegion.Reset(minX, maxX, minY, maxY);

        return pendingRegion;
    }

    private void ReturnPendingRegion(PendingNavigationRegion pendingRegion)
    {
        pendingRegionPool.Push(pendingRegion);
    }

    private void ClearDirtyRegion(
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                NavNode node = nodeGrid[x, y];

                if (node != null)
                {
                    node.connections.Clear();
                }
            }
        }
    }

    private void RebuildDirtyRegion(
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (IsNavigablePosition(x, y))
                {
                    CalculateNodeEdges(x, y);
                }
            }
        }
    }

    public bool IsStandablePosition(int x, int y)
    {
        return gridManager != null && gridManager.IsStandable(x, y);
    }

    /// <summary>
    /// Uma posição navegável pode ter suporte no chão ou na própria Ladder.
    /// Walk/Step continuam usando IsStandablePosition separadamente.
    /// </summary>
    public bool IsNavigablePosition(int x, int y)
    {
        return IsStandablePosition(x, y)
            || IsLadderPosition(x, y);
    }

    public bool IsLadderPosition(int x, int y)
    {
        return gridManager != null
            && gridManager.IsLadder(x, y)
            && gridManager.IsOccupiable(x, y);
    }

    public bool CanProfileUseEdge(
        INavigationProfile profile,
        Vector2Int from,
        NavEdge edge)
    {
        if (edge.targetNode == null)
        {
            return false;
        }

        INavigationProfile effectiveProfile =
            profile != null ? profile : defaultProfile;

        return effectiveProfile != null
            && effectiveProfile.CanUseMovement(
                edge.moveType,
                from,
                edge.targetNode.gridPosition);
    }

    private void CalculateNodeEdges(int x, int y)
    {
        NavNode originNode = nodeGrid[x, y];

        if (originNode == null)
        {
            return;
        }

        AddLadderConnections(x, y, originNode);
        AddHorizontalConnections(x, y, originNode);
    }

    private void AddLadderConnections(
        int x,
        int y,
        NavNode originNode)
    {
        bool originIsLadder = gridManager.IsLadder(x, y);

        if (originIsLadder || gridManager.IsLadder(x, y + 1))
        {
            TryAddLadderConnection(originNode, x, y + 1);
        }

        if (originIsLadder || gridManager.IsLadder(x, y - 1))
        {
            TryAddLadderConnection(originNode, x, y - 1);
        }
    }

    private void TryAddLadderConnection(
        NavNode originNode,
        int targetX,
        int targetY)
    {
        if (!IsInsideGrid(targetX, targetY))
        {
            return;
        }

        if (!IsNavigablePosition(targetX, targetY))
        {
            return;
        }

        NavNode targetNode = nodeGrid[targetX, targetY];

        if (targetNode == null)
        {
            return;
        }

        float cost = defaultProfile.GetMovementCost(
            MovementType.ClimbLadder,
            1f);

        originNode.AddConnection(
            targetNode,
            MovementType.ClimbLadder,
            cost);
    }

    private void AddHorizontalConnections(
        int x,
        int y,
        NavNode originNode)
    {
        int[] directions = { -1, 1 };

        foreach (int direction in directions)
        {
            int targetX = x + direction;

            if (!IsInsideGrid(targetX, y))
            {
                continue;
            }

            if (IsNavigablePosition(targetX, y))
            {
                AddConnection(
                    originNode,
                    targetX,
                    y,
                    MovementType.Walk,
                    1f);

                continue;
            }

            TryAddStepUp(originNode, x, y, targetX);
            TryAddStepDown(originNode, x, y, targetX);
            TryAddJumpGap(originNode, x, y, direction);
        }
    }

    private void TryAddStepUp(
        NavNode originNode,
        int x,
        int y,
        int targetX)
    {
        int maxClimb = MaximumSupportedStepUp;

        for (int step = 1; step <= maxClimb; step++)
        {
            int targetY = y + step;

            if (!IsInsideGrid(targetX, targetY))
            {
                break;
            }

            if (!gridManager.IsPassable(x, targetY))
            {
                break;
            }

            if (!gridManager.IsPassable(targetX, targetY))
            {
                continue;
            }

            if (!gridManager.IsPassable(targetX, targetY + 1))
            {
                break;
            }

            if (!IsStandablePosition(targetX, targetY))
            {
                break;
            }

            float distance = 1f + step * 0.5f;

            AddConnection(
                originNode,
                targetX,
                targetY,
                MovementType.StepUp,
                distance);

            break;
        }
    }

    private void TryAddStepDown(
        NavNode originNode,
        int x,
        int y,
        int targetX)
    {
        for (int drop = 1;
             drop <= MaximumSupportedDropHeight;
             drop++)
        {
            int targetY = y - drop;

            if (!IsInsideGrid(targetX, targetY))
            {
                break;
            }

            if (!gridManager.IsPassable(targetX, y))
            {
                break;
            }

            if (!gridManager.IsPassable(targetX, y + 1))
            {
                break;
            }

            if (!gridManager.IsPassable(targetX, targetY))
            {
                break;
            }

            if (IsStandablePosition(targetX, targetY))
            {
                float distance = 1f + drop * 0.2f;

                AddConnection(
                    originNode,
                    targetX,
                    targetY,
                    MovementType.StepDown,
                    distance);

                break;
            }
        }
    }

    private void TryAddJumpGap(
        NavNode originNode,
        int x,
        int y,
        int direction)
    {
        for (int gap = 2;
             gap <= MaximumSupportedJumpGap + 1;
             gap++)
        {
            int targetX = x + direction * gap;

            if (!IsInsideGrid(targetX, y))
            {
                break;
            }

            if (!IsJumpPathClear(x, y, direction, gap))
            {
                break;
            }

            if (!IsStandablePosition(targetX, y))
            {
                continue;
            }

            float cost = defaultProfile.GetMovementCost(
                MovementType.JumpGap,
                gap);

            NavNode targetNode = nodeGrid[targetX, y];

            if (targetNode == null)
            {
                continue;
            }

            originNode.AddConnection(
                targetNode,
                MovementType.JumpGap,
                cost);

            break;
        }
    }

    private bool IsJumpPathClear(
        int x,
        int y,
        int direction,
        int gap)
    {
        for (int step = 1; step < gap; step++)
        {
            int midX = x + direction * step;

            if (!gridManager.IsPassable(midX, y))
            {
                return false;
            }

            if (!gridManager.IsPassable(midX, y + 1))
            {
                return false;
            }
        }

        return true;
    }

    private void AddConnection(
        NavNode originNode,
        int targetX,
        int targetY,
        MovementType movementType,
        float distance)
    {
        if (!IsInsideGrid(targetX, targetY))
        {
            return;
        }

        NavNode targetNode = nodeGrid[targetX, targetY];

        if (targetNode == null)
        {
            return;
        }

        float cost = defaultProfile.GetMovementCost(
            movementType,
            distance);

        originNode.AddConnection(
            targetNode,
            movementType,
            cost);
    }

    private bool IsInsideGrid(int x, int y)
    {
        return x >= 0
            && x < width
            && y >= 0
            && y < height;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || nodeGrid == null)
        {
            return;
        }

        if (gridManager == null)
        {
            return;
        }

        float cellSize = gridManager.cellSize;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                NavNode node = nodeGrid[x, y];

                if (node == null || node.connections.Count == 0)
                {
                    continue;
                }

                Vector3 nodePosition = new Vector3(
                    x * cellSize + cellSize / 2f,
                    y * cellSize + cellSize / 2f,
                    0f);

                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(nodePosition, 0.15f);

                foreach (NavEdge edge in node.connections)
                {
                    Vector3 targetPosition = new Vector3(
                        edge.targetNode.gridPosition.x * cellSize
                            + cellSize / 2f,
                        edge.targetNode.gridPosition.y * cellSize
                            + cellSize / 2f,
                        0f);

                    switch (edge.moveType)
                    {
                        case MovementType.Walk:
                            Gizmos.color = Color.green;
                            break;

                        case MovementType.StepUp:
                            Gizmos.color = Color.yellow;
                            break;

                        case MovementType.StepDown:
                            Gizmos.color = Color.gray;
                            break;

                        case MovementType.JumpGap:
                            Gizmos.color = Color.magenta;
                            break;

                        case MovementType.ClimbLadder:
                            Gizmos.color = Color.blue;
                            break;
                    }

                    Gizmos.DrawLine(nodePosition, targetPosition);
                }
            }
        }
    }
}