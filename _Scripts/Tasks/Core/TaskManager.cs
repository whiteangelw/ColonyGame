using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class TaskManager : MonoBehaviour
{
    private static readonly ProfilerMarker SelectTaskMarker =
        new ProfilerMarker("Colony.Tasks.SelectAndAssign");
    public static TaskManager Instance { get; private set; }

    public event Action<Task> OnTaskAdded;
    public event Action<Task> OnTaskRemoved;

    private readonly List<Task> pendingTasks = new List<Task>();
    private readonly Dictionary<TaskType, ITaskHandler> handlers =
        new Dictionary<TaskType, ITaskHandler>();
    private readonly List<TaskCandidate> taskCandidateBuffer =
        new List<TaskCandidate>(512);
    private readonly List<Task> batchCandidateBuffer = new List<Task>(64);
    private readonly List<Task> invalidTaskBuffer = new List<Task>(64);
    private Comparison<Task> compareBatchByDistance;

    // O diagnóstico detalhado cria strings a cada candidato rejeitado.
    // Reative temporariamente no código somente ao investigar construção.
    private static readonly bool EnableBuildTaskDiagnostics = false;

    [Header("Orçamento de busca")]
    [SerializeField, Min(1)] private int maximumTaskSearchesPerFrame = 2;

    private int taskSearchBudgetFrame = -1;
    private int taskSearchesThisFrame;
    private int lastInvalidTaskCleanupFrame = -1;
    private bool isRemovingInvalidTasks;
    private Vector2Int candidateSortOrigin;
    private GridManager subscribedGrid;

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
        compareBatchByDistance = CompareByDistance;
    }

    private void Start()
    {
        TrySubscribeToGrid();
        RemoveInvalidTasks(true);
    }

    private void OnDestroy()
    {
        if (subscribedGrid != null)
        {
            subscribedGrid.OnTileChanged -= HandleTileChanged;
            subscribedGrid.OnGridRebuilt -= HandleGridRebuilt;
        }
    }

    private void TrySubscribeToGrid()
    {
        if (subscribedGrid == GridManager.Instance) return;
        if (subscribedGrid != null)
        {
            subscribedGrid.OnTileChanged -= HandleTileChanged;
            subscribedGrid.OnGridRebuilt -= HandleGridRebuilt;
        }

        subscribedGrid = GridManager.Instance;
        if (subscribedGrid != null)
        {
            subscribedGrid.OnTileChanged += HandleTileChanged;
            subscribedGrid.OnGridRebuilt += HandleGridRebuilt;
        }
    }

    private void HandleTileChanged(int x, int y, TileType type)
    {
        RemoveInvalidTasks(true);
    }

    private void HandleGridRebuilt()
    {
        RemoveInvalidTasks(true);
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

    public void AddTask(
        Vector2Int gridPos,
        TaskType type,
        GridLayer targetLayer = GridLayer.Terrain)
    {
        bool alreadyExists = type == TaskType.Dismantle
            ? GetTaskAt(gridPos, type, targetLayer) != null
            : GetTaskAt(gridPos) != null;

        if (alreadyExists) return;

        int categoryPriority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(type) : 5;
        Task newTask = new Task(
            gridPos,
            type,
            TileType.Empty,
            null,
            categoryPriority,
            targetLayer);

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

        if (!item.IsReadyForHaul)
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

    public bool CancelTasksAt(Vector2Int position)
    {
        bool cancelled = false;
        List<Task> matches = pendingTasks.FindAll(task =>
            task != null && task.gridPosition == position);

        foreach (Task task in matches)
        {
            PerformanceMetricsService.RecordGlobalObjectSearch();
            DuplicantController[] duplicants = FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (DuplicantController duplicant in duplicants)
            {
                if (duplicant != null && duplicant.currentTask == task)
                    duplicant.CancelCurrentTaskExecution(
                        TaskInterruptionOrigin.ManualCancellation);
            }
            task.isAssigned = false;
            RemoveTask(task);
            cancelled = true;
        }
        return cancelled;
    }

    public int CancelTasksForBlueprint(ConstructionBlueprint blueprint)
    {
        if (blueprint == null) return 0;

        List<Task> matches = pendingTasks.FindAll(task =>
            task != null && task.targetBlueprint == blueprint);

        foreach (Task task in matches)
        {
            InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.BlueprintCancelled,
                true);
            task.isAssigned = false;
            RemoveTask(task);
        }

        return matches.Count;
    }

    private static void InterruptAssignedTask(
        Task task,
        TaskInterruptionOrigin origin,
        bool preserveCarriedInventory = false,
        bool skipWorkerAlreadyExecuting = false)
    {
        if (task == null || !task.isAssigned) return;

        PerformanceMetricsService.RecordGlobalObjectSearch();
        DuplicantController[] duplicants =
            FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (DuplicantController duplicant in duplicants)
        {
            if (duplicant != null && duplicant.currentTask == task)
            {
                if (skipWorkerAlreadyExecuting
                    && duplicant.currentState ==
                        DuplicantController.WorkerState.Working)
                {
                    continue;
                }

                duplicant.CancelCurrentTaskExecution(
                    origin,
                    preserveCarriedInventory);
            }
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
        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (task != null && task.gridPosition == gridPos) return task;
        }

        return null;
    }

    public Task GetTaskAt(
        Vector2Int gridPos,
        TaskType type,
        GridLayer targetLayer)
    {
        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (task != null
                && task.gridPosition == gridPos
                && task.type == type
                && task.targetLayer == targetLayer)
            {
                return task;
            }
        }

        return null;
    }

    public bool ContainsTask(Task task)
    {
        return task != null && pendingTasks.Contains(task);
    }

    public List<Task> GetTasksSnapshot()
    {
        return new List<Task>(pendingTasks);
    }

    public void ClearTasksForLoad()
    {
        for (int i = pendingTasks.Count - 1; i >= 0; i--)
        {
            Task task = pendingTasks[i];
            pendingTasks.RemoveAt(i);

            if (task != null)
            {
                task.isAssigned = false;
                OnTaskRemoved?.Invoke(task);
            }
        }

        taskCandidateBuffer.Clear();
        batchCandidateBuffer.Clear();
        invalidTaskBuffer.Clear();
    }

    public bool TryAcquireTaskSearchSlot()
    {
        if (taskSearchBudgetFrame != Time.frameCount)
        {
            taskSearchBudgetFrame = Time.frameCount;
            taskSearchesThisFrame = 0;
        }

        if (taskSearchesThisFrame >= maximumTaskSearchesPerFrame)
        {
            return false;
        }

        taskSearchesThisFrame++;
        return true;
    }

    public Task GetNextTaskFor(
        Vector2Int dupeGridPos,
        out List<Vector2Int> calculatedPath,
        DuplicantCapabilityProfile profile = null,
        DuplicantWorkProfile workProfile = null)
    {
        using (SelectTaskMarker.Auto())
        {
            long startedAt = PerformanceMetricsService.BeginSample();
            try
            {
                return GetNextTaskForCore(
                    dupeGridPos,
                    out calculatedPath,
                    profile,
                    workProfile);
            }
            finally
            {
                PerformanceMetricsService.EndSample(
                    PerformanceMetric.TaskSelection,
                    startedAt);
            }
        }
    }

    private Task GetNextTaskForCore(
        Vector2Int dupeGridPos,
        out List<Vector2Int> calculatedPath,
        DuplicantCapabilityProfile profile,
        DuplicantWorkProfile workProfile)
    {
        RemoveInvalidTasks();

        calculatedPath = null;
        taskCandidateBuffer.Clear();

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (task == null || task.isAssigned) continue;

            RefreshDynamicTaskPosition(task);
            taskCandidateBuffer.Add(new TaskCandidate(
                task,
                GetTaskSelectionScore(task, dupeGridPos, workProfile),
                SquaredGridDistance(dupeGridPos, task.gridPosition)));
        }

        taskCandidateBuffer.Sort(TaskCandidateComparer.Instance);

        for (int i = 0; i < taskCandidateBuffer.Count; i++)
        {
            Task task = taskCandidateBuffer[i].Task;
            bool isGroundHaul = task.type == TaskType.HaulResource
                && task.targetBlueprint == null;

            // Haul de chão precisa alcançar exatamente o item. Tarefas de
            // interação são validadas pelos candidatos reais no utility.
            if (isGroundHaul
                && ReachabilityManager.Instance != null
                && ReachabilityManager.Instance.IsReady)
            {
                if (!ReachabilityManager.Instance.CanReachExact(
                        dupeGridPos,
                        task.gridPosition,
                        profile))
                {
                    LogTaskRejection(task, "o item exato não está alcançável");
                    continue;
                }
            }

            ITaskHandler handler = GetHandler(task.type);
            if (handler == null || !handler.CanExecute(null, task))
            {
                LogTaskRejection(task, "o handler recusou o estado atual");
                continue;
            }

            List<Vector2Int> path = TaskNavigationUtility.GetPathToTask(
                dupeGridPos,
                task,
                profile
            );

            if (path == null)
            {
                LogTaskRejection(
                    task,
                    "nenhum interaction candidate alcançável gerou path");
                continue;
            }

            task.isAssigned = true;
            LogTaskRejection(task, "aceita; path encontrado");
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

        batchCandidateBuffer.Clear();

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

            batchCandidateBuffer.Add(task);
        }

        candidateSortOrigin = dupeGridPos;
        batchCandidateBuffer.Sort(compareBatchByDistance);

        int checks = Mathf.Min(
            maximumBatchPathChecks,
            batchCandidateBuffer.Count
        );

        for (int i = 0; i < checks; i++)
        {
            Task candidate = batchCandidateBuffer[i];

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

    private int CompareByDistance(Task a, Task b)
    {
        return SquaredGridDistance(candidateSortOrigin, a.gridPosition)
            .CompareTo(
                SquaredGridDistance(candidateSortOrigin, b.gridPosition)
            );
    }

    public int GetTaskSelectionScore(
        Task task,
        Vector2Int duplicantPosition,
        DuplicantWorkProfile workProfile = null)
    {
        if (task == null)
        {
            return int.MinValue;
        }

        int globalPriorityScore = task.priority * 1000;
        int affinityScore = workProfile != null
            ? workProfile.GetAffinity(task.type) * 100
            : 0;

        int gridDistance = Mathf.Abs(
            duplicantPosition.x - task.gridPosition.x
        ) + Mathf.Abs(
            duplicantPosition.y - task.gridPosition.y
        );

        int distancePenalty = Mathf.Min(gridDistance, 99);

        return globalPriorityScore
            + affinityScore
            - distancePenalty;
    }

    private static void RefreshDynamicTaskPosition(Task task)
    {
        if (task.type != TaskType.HaulResource
            || task.targetBlueprint != null
            || task.targetItem == null
            || GridManager.Instance == null)
        {
            return;
        }

        task.gridPosition = GridManager.Instance.WorldToGridPosition(
            task.targetItem.transform.position
        );
    }

    private void RemoveInvalidTasks(bool force = false)
    {
        TrySubscribeToGrid();
        if (isRemovingInvalidTasks)
        {
            return;
        }

        if (!force && lastInvalidTaskCleanupFrame == Time.frameCount)
        {
            return;
        }

        lastInvalidTaskCleanupFrame = Time.frameCount;

        if (GridManager.Instance == null)
        {
            return;
        }

        isRemovingInvalidTasks = true;
        invalidTaskBuffer.Clear();
        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (!IsTaskValid(task)) invalidTaskBuffer.Add(task);
        }

        foreach (Task task in invalidTaskBuffer)
        {
            if (!pendingTasks.Contains(task))
            {
                continue;
            }

            InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.TaskInvalidated,
                false,
                true);
            task.isAssigned = false;
            RemoveTask(task);
        }

        isRemovingInvalidTasks = false;
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

                if (task.groundHaulPhase ==
                    GroundHaulPhase.CarryingToStorage)
                {
                    return task.isAssigned
                        && task.groundHaulRunner != null
                        && task.groundHaulRunner.CanContinueGroundHaul(task);
                }

                return task.targetItem != null
                    && task.targetItem.gameObject.activeInHierarchy
                    && task.targetItem.IsReadyForHaul;

            case TaskType.BuildTile:
                return task.targetBlueprint != null
                    && task.targetBlueprint.CurrentState ==
                        BlueprintState.ReadyToBuild;

            case TaskType.Dig:
                if (task.targetBlueprint != null
                    && task.targetBlueprint.CurrentState !=
                        BlueprintState.WaitingForClearance)
                {
                    return false;
                }

                return WorldInteractionService.Instance != null
                    && WorldInteractionService.Instance.CanDigTile(
                        task.gridPosition.x,
                        task.gridPosition.y,
                        out _);

            case TaskType.Dismantle:
                return WorldInteractionService.Instance != null
                    && WorldInteractionService.Instance.CanDismantleAt(
                        task.gridPosition,
                        task.targetLayer);

            default:
                return false;
        }
    }

    private void LogTaskRejection(Task task, string reason)
    {
        if (!EnableBuildTaskDiagnostics
            || task == null
            || task.targetBlueprint == null)
        {
            return;
        }

        ConstructionBlueprint blueprint = task.targetBlueprint;
        Tile terrain = GridManager.Instance?.GetTile(blueprint.gridPosition);
        string message =
            $"[TaskManagerDiagnostic] Task={task.type}; "
            + $"BlueprintState={blueprint.CurrentState}; "
            + $"Target={blueprint.gridPosition}; "
            + $"Terrain={(terrain != null ? terrain.type.ToString() : "fora do grid")}; "
            + $"Resultado={reason}";

        Debug.Log(message);
    }

    private readonly struct TaskCandidate
    {
        public readonly Task Task;
        public readonly int Score;
        public readonly int DistanceSquared;

        public TaskCandidate(Task task, int score, int distanceSquared)
        {
            Task = task;
            Score = score;
            DistanceSquared = distanceSquared;
        }
    }

    private sealed class TaskCandidateComparer : IComparer<TaskCandidate>
    {
        public static readonly TaskCandidateComparer Instance =
            new TaskCandidateComparer();

        public int Compare(TaskCandidate a, TaskCandidate b)
        {
            int scoreComparison = b.Score.CompareTo(a.Score);
            return scoreComparison != 0
                ? scoreComparison
                : a.DistanceSquared.CompareTo(b.DistanceSquared);
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

        if (candidate.type == TaskType.Dismantle)
        {
            return existing.gridPosition == candidate.gridPosition
                && existing.targetLayer == candidate.targetLayer;
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
