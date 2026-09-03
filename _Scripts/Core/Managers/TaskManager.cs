using System;
using System.Collections.Generic;
using UnityEngine;

public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    public event Action<Task> OnTaskAdded;
    public event Action<Task> OnTaskRemoved;

    private readonly List<Task> pendingTasks = new List<Task>();
    private readonly Dictionary<TaskType, ITaskHandler> handlers =
        new Dictionary<TaskType, ITaskHandler>();

    [Header("Transporte em lote")]
    [SerializeField, Min(1)]
    private int maximumBatchPathChecks = 12;

    [SerializeField, Min(1)]
    private int maximumBatchPickupDistance = 12;

    public int PendingTaskCount => pendingTasks.Count;

    public int AssignedTaskCount
    {
        get
        {
            int count = 0;

            foreach (Task task in pendingTasks)
            {
                if (task != null && task.isAssigned)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        RegisterHandler(new DigTaskHandler());
        RegisterHandler(new BuildTaskHandler());
        RegisterHandler(new HaulTaskHandler());
        RegisterHandler(new DismantleTaskHandler());
    }

    public void RegisterHandler(ITaskHandler handler)
    {
        if (handler != null && !handlers.ContainsKey(handler.HandledType))
        {
            handlers.Add(handler.HandledType, handler);
        }
    }

    public ITaskHandler GetHandler(TaskType type)
    {
        handlers.TryGetValue(type, out ITaskHandler handler);
        return handler;
    }

    public void AddTask(Vector2Int gridPos, TaskType type)
    {
        if (GetTaskAt(gridPos) != null) return;

        int categoryPriority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(type) : 5;
        Task newTask = new Task(gridPos, type, TileType.Empty, null, categoryPriority);

        pendingTasks.Add(newTask);
        OnTaskAdded?.Invoke(newTask);
    }

    public void AddTask(Task task)
    {
        if (task == null) return;
        if (pendingTasks.Exists(existing => IsSameTask(existing, task))) return;

        pendingTasks.Add(task);
        OnTaskAdded?.Invoke(task);
    }

    public void AddBuildTask(Vector2Int gridPos, TileType tileToBuild)
    {
        if (GetTaskAt(gridPos) != null) return;

        int categoryPriority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(TaskType.BuildTile) : 5;
        Task buildTask = new Task(gridPos, TaskType.BuildTile, tileToBuild, null, categoryPriority);
        pendingTasks.Add(buildTask);
        OnTaskAdded?.Invoke(buildTask);
    }

    public void AddHaulTask(ResourceItem item)
    {
        if (item == null || GridManager.Instance == null) return;

        Vector2Int gridPos = GridManager.Instance.WorldToGridPosition(
            item.transform.position
        );

        if (!item.IsReadyForHaul
            || StructureManager.Instance == null
            || !StructureManager.Instance.HasReachableStorageFor(
                item.type,
                1,
                gridPos
            ))
        {
            return;
        }

        bool alreadyExists = pendingTasks.Exists(t => t.type == TaskType.HaulResource && t.targetItem == item);

        if (!alreadyExists)
        {
            int categoryPriority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(TaskType.HaulResource) : 5;
            Task haulTask = new Task(gridPos, TaskType.HaulResource, TileType.Empty, item, categoryPriority);
            pendingTasks.Add(haulTask);
            OnTaskAdded?.Invoke(haulTask);
        }
    }

    public void RemoveTask(Task task)
    {
        if (task != null && pendingTasks.Contains(task))
        {
            pendingTasks.Remove(task);
            OnTaskRemoved?.Invoke(task);
        }
    }

    public void ReleaseTask(Task task)
    {
        if (task != null && pendingTasks.Contains(task))
        {
            task.isAssigned = false;
        }
    }

    public Task GetTaskAt(Vector2Int gridPos)
    {
        return pendingTasks.Find(t => t.gridPosition == gridPos);
    }

    public bool ContainsTask(Task task)
    {
        return task != null && pendingTasks.Contains(task);
    }

    public Task GetNextTaskFor(Vector2Int dupeGridPos, out List<Vector2Int> calculatedPath, DuplicantCapabilityProfile profile = null)
    {
        RemoveInvalidTasks();

        calculatedPath = null;
        List<Task> candidates = new List<Task>();

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (task == null || task.isAssigned) continue;

            ITaskHandler handler = GetHandler(task.type);
            if (handler == null || !handler.CanExecute(null, task)) continue;

            candidates.Add(task);
        }

        candidates.Sort((a, b) =>
        {
            int priorityComparison = b.priority.CompareTo(a.priority);
            if (priorityComparison != 0) return priorityComparison;

            int distanceA = SquaredGridDistance(dupeGridPos, a.gridPosition);
            int distanceB = SquaredGridDistance(dupeGridPos, b.gridPosition);
            return distanceA.CompareTo(distanceB);
        });

        foreach (Task task in candidates)
        {
            if (ReachabilityManager.Instance != null
                && ReachabilityManager.Instance.IsReady)
            {
                bool isGroundHaul = task.type == TaskType.HaulResource
                    && task.targetBlueprint == null;

                bool canReach = isGroundHaul
                    ? ReachabilityManager.Instance.CanReachExact(
                        dupeGridPos,
                        task.gridPosition,
                        profile
                    )
                    : ReachabilityManager.Instance.CanReach(
                        dupeGridPos,
                        task.gridPosition,
                        profile
                    );

                if (!canReach) continue;
            }

            List<Vector2Int> path = TaskNavigationUtility.GetPathToTask(
                dupeGridPos,
                task,
                profile
            );

            if (path == null) continue;

            task.isAssigned = true;
            calculatedPath = path;
            return task;
        }

        return null;
    }

    public bool TryAssignAdditionalGroundHaulTask(
        Vector2Int dupeGridPos,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        out Task selectedTask,
        out List<Vector2Int> calculatedPath)
    {
        RemoveInvalidTasks();

        selectedTask = null;
        calculatedPath = null;

        List<Task> candidates = new List<Task>();

        foreach (Task task in pendingTasks)
        {
            if (task == null
                || task.isAssigned
                || task.type != TaskType.HaulResource
                || task.targetBlueprint != null
                || task.targetItem == null
                || task.targetItem.type != resourceType
                || !task.targetItem.IsReadyForHaul)
            {
                continue;
            }

            task.gridPosition = GridManager.Instance.WorldToGridPosition(
                task.targetItem.transform.position
            );

            int maximumDistanceSquared =
                maximumBatchPickupDistance * maximumBatchPickupDistance;

            if (SquaredGridDistance(dupeGridPos, task.gridPosition)
                > maximumDistanceSquared)
            {
                continue;
            }

            candidates.Add(task);
        }

        candidates.Sort((a, b) =>
            SquaredGridDistance(dupeGridPos, a.gridPosition)
                .CompareTo(SquaredGridDistance(dupeGridPos, b.gridPosition))
        );

        int checks = Mathf.Min(maximumBatchPathChecks, candidates.Count);

        for (int i = 0; i < checks; i++)
        {
            Task candidate = candidates[i];

            if (ReachabilityManager.Instance != null
                && ReachabilityManager.Instance.IsReady
                && !ReachabilityManager.Instance.CanReachExact(
                    dupeGridPos,
                    candidate.gridPosition,
                    profile))
            {
                continue;
            }

            List<Vector2Int> path = PathfindingAStar.Instance?.FindPath(
                dupeGridPos,
                candidate.gridPosition,
                profile
            );

            if (path == null)
            {
                continue;
            }

            candidate.isAssigned = true;
            selectedTask = candidate;
            calculatedPath = path;
            return true;
        }

        return false;
    }

    private static int SquaredGridDistance(Vector2Int a, Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private void RemoveInvalidTasks()
    {
        if (GridManager.Instance == null)
        {
            return;
        }

        for (int i = pendingTasks.Count - 1; i >= 0; i--)
        {
            Task task = pendingTasks[i];
            bool isInvalid = !IsTaskValid(task);

            if (!isInvalid)
            {
                continue;
            }

            pendingTasks.RemoveAt(i);

            if (task != null)
            {
                OnTaskRemoved?.Invoke(task);
            }
        }
    }

    public bool IsTaskValid(Task task)
    {
        if (task == null || GridManager.Instance == null)
        {
            return false;
        }

        Tile tile = GridManager.Instance.GetTile(task.gridPosition);

        switch (task.type)
        {
            case TaskType.HaulResource:
                if (task.targetBlueprint != null)
                {
                    return task.targetBlueprint.CurrentState ==
                               BlueprintState.WaitingMaterials
                        && task.targetBlueprint.deliveredAmount <
                           task.targetBlueprint.requiredAmount;
                }

                return task.targetItem != null
                    && task.targetItem.gameObject.activeInHierarchy
                    && task.targetItem.IsReadyForHaul;

            case TaskType.BuildTile:
                return task.targetBlueprint != null
                    && task.targetBlueprint.CurrentState ==
                        BlueprintState.ReadyToBuild;

            case TaskType.Dig:
                return tile != null
                    && tile.type != TileType.Empty
                    && tile.type != TileType.Bedrock
                    && tile.type != TileType.Chest
                    && tile.type != TileType.Ladder;

            case TaskType.Dismantle:
                return tile != null
                    && (tile.type == TileType.Chest
                        || tile.type == TileType.Ladder);

            default:
                return false;
        }
    }

    private static bool IsSameTask(Task existing, Task candidate)
    {
        if (existing == null || candidate == null
            || existing.type != candidate.type)
        {
            return false;
        }

        if (candidate.type == TaskType.HaulResource)
        {
            if (candidate.targetBlueprint != null)
            {
                return existing.targetBlueprint == candidate.targetBlueprint;
            }

            return candidate.targetItem != null
                && existing.targetItem == candidate.targetItem;
        }

        return existing.gridPosition == candidate.gridPosition;
    }

    public void ExecuteTask(Task task, DuplicantController dupe)
    {
        if (task == null) return;

        ITaskHandler handler = GetHandler(task.type);
        handler?.StartTask(dupe, task, null);
    }

    public List<Vector2Int> GetPathToTask(Vector2Int startPos, Task task, DuplicantCapabilityProfile profile = null)
    {
        return TaskNavigationUtility.GetPathToTask(startPos, task, profile);
    }
}
