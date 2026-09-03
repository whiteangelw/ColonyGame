using System.Collections.Generic;
using UnityEngine;

public class PathfindingAStar : MonoBehaviour
{
    public static PathfindingAStar Instance { get; private set; }

    private NavGraphGenerator navGraph;
    private MinHeap<PathNodeAdapter> openHeap;

    // Reutilizado entre buscas para reduzir Garbage Collection.
    private PathNodeAdapter[,] nodeAdapterGrid;

    private int currentSearchId;

    private int initializedWidth;
    private int initializedHeight;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        navGraph = NavGraphGenerator.Instance;

        InitializeAdapterGrid();
    }

    public void InitializeAdapterGrid()
    {
        GridManager gridManager = GridManager.Instance;

        if (gridManager == null)
        {
            return;
        }

        int width = gridManager.width;
        int height = gridManager.height;

        if (width <= 0 || height <= 0)
        {
            Debug.LogError(
                "[PathfindingAStar] Dimensões inválidas do Grid."
            );

            return;
        }

        initializedWidth = width;
        initializedHeight = height;

        openHeap = new MinHeap<PathNodeAdapter>(
            width * height
        );

        nodeAdapterGrid = new PathNodeAdapter[
            width,
            height
        ];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                nodeAdapterGrid[x, y] =
                    new PathNodeAdapter();
            }
        }

        currentSearchId = 0;
    }

    public List<Vector2Int> FindPath(
        Vector2Int startPos,
        Vector2Int targetPos,
        DuplicantCapabilityProfile profile = null)
    {
        if (!CanSearch())
        {
            return null;
        }

        if (!IsInsideGrid(startPos)
            || !IsInsideGrid(targetPos))
        {
            return null;
        }

        NavNode startNode = navGraph.GetNode(startPos);
        NavNode targetNode = navGraph.GetNode(targetPos);
        DuplicantCapabilityProfile effectiveProfile =
            profile != null ? profile : navGraph.DefaultProfile;

        if (startNode == null
            || targetNode == null
            || effectiveProfile == null)
        {
            return null;
        }

        // O destino precisa ser um Node navegável.
        if (!navGraph.IsStandablePosition(
                targetPos.x,
                targetPos.y))
        {
            return null;
        }

        // Reinicializa os adapters antes que o identificador da busca se repita.
        if (currentSearchId == int.MaxValue)
        {
            InitializeAdapterGrid();
        }

        currentSearchId++;

        openHeap.Clear();

        PathNodeAdapter startAdapter =
            GetAdapter(startNode);

        startAdapter.ResetForSearch(
            currentSearchId,
            startNode
        );

        startAdapter.gCost = 0f;

        startAdapter.hCost =
            GetHeuristic(startPos, targetPos, effectiveProfile);

        startAdapter.cameFrom = null;
        startAdapter.isInOpenSet = true;

        openHeap.Add(startAdapter);

        while (openHeap.Count > 0)
        {
            PathNodeAdapter current =
                openHeap.RemoveFirst();

            current.isInOpenSet = false;

            if (current.isClosed)
            {
                continue;
            }

            current.isClosed = true;

            if (current.Node == targetNode)
            {
                return RetracePath(
                    startAdapter,
                    current
                );
            }

            foreach (NavEdge edge
                     in current.Node.connections)
            {
                if (edge.targetNode == null)
                {
                    continue;
                }

                if (!navGraph.CanProfileUseEdge(
                        effectiveProfile,
                        current.Node.gridPosition,
                        edge))
                {
                    continue;
                }

                PathNodeAdapter neighbor =
                    GetAdapter(edge.targetNode);

                neighbor.ResetForSearch(
                    currentSearchId,
                    edge.targetNode
                );

                if (neighbor.isClosed)
                {
                    continue;
                }

                float traversalCost =
                    effectiveProfile.GetEdgeMovementCost(
                        edge.moveType,
                        current.Node.gridPosition,
                        edge.targetNode.gridPosition
                    );

                float newGCost = current.gCost + traversalCost;

                if (newGCost >= neighbor.gCost)
                {
                    continue;
                }

                neighbor.gCost = newGCost;

                neighbor.hCost =
                    GetHeuristic(
                        neighbor.Node.gridPosition,
                        targetPos,
                        effectiveProfile
                    );

                neighbor.cameFrom = current;

                if (!neighbor.isInOpenSet)
                {
                    neighbor.isInOpenSet = true;

                    openHeap.Add(neighbor);
                }
                else
                {
                    openHeap.UpdateItem(neighbor);
                }
            }
        }

        return null;
    }

    private bool CanSearch()
    {
        if (GridManager.Instance == null)
        {
            return false;
        }

        if (navGraph == null)
        {
            navGraph = NavGraphGenerator.Instance;
        }

        if (navGraph == null
            || !navGraph.IsGraphReady)
        {
            return false;
        }

        if (nodeAdapterGrid == null
            || openHeap == null
            || GridSizeChanged())
        {
            InitializeAdapterGrid();
        }

        return nodeAdapterGrid != null
            && openHeap != null;
    }

    private bool GridSizeChanged()
    {
        GridManager gridManager =
            GridManager.Instance;

        return gridManager.width != initializedWidth
            || gridManager.height != initializedHeight;
    }

    private bool IsInsideGrid(Vector2Int position)
    {
        GridManager gridManager =
            GridManager.Instance;

        return position.x >= 0
            && position.x < gridManager.width
            && position.y >= 0
            && position.y < gridManager.height;
    }

    private PathNodeAdapter GetAdapter(NavNode node)
    {
        Vector2Int position =
            node.gridPosition;

        return nodeAdapterGrid[
            position.x,
            position.y
        ];
    }

    private float GetHeuristic(
        Vector2Int a,
        Vector2Int b,
        DuplicantCapabilityProfile profile)
    {
        int gridDistance = Mathf.Abs(a.x - b.x)
            + Mathf.Abs(a.y - b.y);

        return gridDistance * profile.GetMinimumCostPerGridUnit();
    }

    private List<Vector2Int> RetracePath(
        PathNodeAdapter start,
        PathNodeAdapter end)
    {
        List<Vector2Int> path =
            new List<Vector2Int>();

        PathNodeAdapter current = end;

        int safetyCounter = 0;
        int maxSteps =
            initializedWidth * initializedHeight;

        while (current != start)
        {
            if (current == null
                || current.cameFrom == null)
            {
                return null;
            }

            path.Add(
                current.Node.gridPosition
            );

            current = current.cameFrom;

            safetyCounter++;

            if (safetyCounter > maxSteps)
            {
                Debug.LogError(
                    "[PathfindingAStar] Caminho corrompido detectado."
                );

                return null;
            }
        }

        path.Reverse();

        return path;
    }

    private class PathNodeAdapter
        : IHeapItem<PathNodeAdapter>
    {
        public NavNode Node { get; private set; }

        public float gCost;
        public float hCost;

        public float FCost =>
            gCost + hCost;

        public PathNodeAdapter cameFrom;

        public bool isInOpenSet;
        public bool isClosed;

        private int lastSearchId = -1;

        public int HeapIndex { get; set; }

        public void ResetForSearch(
            int searchId,
            NavNode node)
        {
            if (lastSearchId == searchId)
            {
                return;
            }

            lastSearchId = searchId;

            Node = node;

            gCost = float.MaxValue;
            hCost = 0f;

            cameFrom = null;

            isInOpenSet = false;
            isClosed = false;

            HeapIndex = -1;
        }

        public int CompareTo(
            PathNodeAdapter other)
        {
            int compare =
                FCost.CompareTo(other.FCost);

            if (compare == 0)
            {
                compare =
                    hCost.CompareTo(other.hCost);
            }

            return compare;
        }
    }
}
