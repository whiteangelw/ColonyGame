using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class TaskManager : MonoBehaviour
{
    private readonly TaskValidationService taskValidator =
    new TaskValidationService();
    private readonly TaskFactory taskFactory =
    new TaskFactory();
    private static readonly ProfilerMarker SelectTaskMarker =
        new ProfilerMarker("Colony.Tasks.SelectAndAssign");

    public static TaskManager Instance { get; private set; }

    public event Action<Task> OnTaskAdded
    {
        add => taskRegistry.TaskAdded += value;
        remove => taskRegistry.TaskAdded -= value;
    }

    public event Action<Task> OnTaskRemoved
    {
        add => taskRegistry.TaskRemoved += value;
        remove => taskRegistry.TaskRemoved -= value;
    }

    private readonly TaskRegistry taskRegistry =
       new TaskRegistry();

    private readonly TaskCancellationService taskCancellation =
        new TaskCancellationService();

    private List<Task> pendingTasks => taskRegistry.Tasks;

    private readonly TaskHandlerRegistry handlerRegistry =
        new TaskHandlerRegistry();

    private readonly TaskSelectionService taskSelector =
        new TaskSelectionService();

    [SerializeField]
    private bool enableBuildTaskDiagnostics;

    [Header("Trabalho")]
    [Tooltip("Durações base para tarefas que não possuem um alvo com definição própria.")]
    [SerializeField] private TaskWorkSettingsSO workSettings;

    private readonly TaskSelectionDiagnostics selectionDiagnostics =
        new TaskSelectionDiagnostics();

    [Header("Orçamento de busca")]
    [SerializeField, Min(1)]
    private int maximumTaskSearchesPerFrame = 2;
    private readonly TaskSearchBudget taskSearchBudget =
        new TaskSearchBudget();
    private readonly TaskBatchSelectionService batchSelector =
    new TaskBatchSelectionService();

    private readonly TaskCleanupService taskCleanup =
        new TaskCleanupService();
    private readonly TaskGridSubscription gridSubscription =
       new TaskGridSubscription();

    [Header("Transporte em lote")]
    [SerializeField, Min(1)]
    private int maximumBatchPathChecks = 12;

    [SerializeField, Min(1)]
    private int maximumBatchPickupDistance = 12;

    [Header("Entrega de blueprints em lote")]
    [SerializeField, Min(1)]
    private int maximumBlueprintDeliveriesPerRun = 12;

    [SerializeField, Min(1)]
    private int maximumBlueprintDeliveryDistance = 32;

    public int MaximumBlueprintDeliveriesPerRun =>
        Mathf.Max(1, maximumBlueprintDeliveriesPerRun);

    public int MaximumBlueprintDeliveryDistance =>
        Mathf.Max(1, maximumBlueprintDeliveryDistance);

    public int PendingTaskCount => taskRegistry.Count;

    public int AssignedTaskCount => taskRegistry.AssignedCount;

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

        taskFactory.Configure(workSettings);

        RegisterHandler(new DigTaskHandler());
        RegisterHandler(new BuildTaskHandler());
        RegisterHandler(new HaulTaskHandler());
        RegisterHandler(new DismantleTaskHandler());
        RegisterHandler(new HarvestTaskHandler());
        RegisterHandler(new HarvestTaskHandler(TaskType.Chop));
        RegisterHandler(new OperateMachineTaskHandler());

    }

    private void Start()
    {
        RefreshGridSubscription();
        RemoveInvalidTasks(true);
    }

    private void OnDestroy()
    {
        gridSubscription.Disconnect(
            HandleTileChanged,
            HandleGridRebuilt);
    }

    private void RefreshGridSubscription()
    {
        gridSubscription.Refresh(
            HandleTileChanged,
            HandleGridRebuilt);
    }

    private void HandleTileChanged(
        int x,
        int y,
        TileType type)
    {
        RemoveInvalidTasks(true);
    }

    private void HandleGridRebuilt()
    {
        RemoveInvalidTasks(true);
    }

    public void RegisterHandler(ITaskHandler handler)
    {
        handlerRegistry.Register(handler);
    }

    public ITaskHandler GetHandler(TaskType type)
    {
        return handlerRegistry.Get(type);
    }

    public void AddTask(
        Vector2Int gridPos,
        TaskType type,
        GridLayer targetLayer = GridLayer.Terrain)
    {
        bool alreadyExists = type == TaskType.Dismantle
            ? GetTaskAt(gridPos, type, targetLayer) != null
            : GetTaskAt(gridPos) != null;

        if (alreadyExists)
        {
            return;
        }

        Task newTask = taskFactory.CreateTask(
            gridPos,
            type,
            targetLayer);

        taskRegistry.Add(newTask);
    }

    public void AddTask(Task task)
    {
        taskRegistry.Add(task);
    }

    public bool AddHarvestTask(
        FloraEntity flora,
        int priorityOverride = -1)
    {
        if (flora == null
            || GetTaskAt(flora.GridPosition) != null)
        {
            return false;
        }

        Task harvestTask = taskFactory.CreateHarvestTask(
            flora,
            priorityOverride);
        if (harvestTask == null) return false;

        taskRegistry.Add(harvestTask);
        return true;
    }

    public void AddBuildTask(
        Vector2Int gridPos,
        TileType tileToBuild)
    {
        if (GetTaskAt(gridPos) != null)
        {
            return;
        }

        Task buildTask = taskFactory.CreateBuildTask(
            gridPos,
            tileToBuild);

        taskRegistry.Add(buildTask);
    }

    public void AddHaulTask(ResourceItem item)
    {
        Task haulTask = taskFactory.CreateGroundHaulTask(item);

        if (haulTask != null)
        {
            taskRegistry.Add(haulTask);
        }
    }

    public void AddMachineSupplyTask(
        ProductionMachineBehaviour machine,
        ResourceType resourceType)
    {
        AddResourceDeliveryTask(machine, resourceType);
    }

    public void AddResourceDeliveryTask(
        IResourceDeliveryTarget target,
        ResourceType resourceType)
    {
        if (target == null || HasDeliveryTask(target, resourceType))
        {
            return;
        }

        Task task = new Task(
            target.GridPosition,
            TaskType.HaulResource,
            TileType.Empty,
            null,
            PriorityManager.Instance != null
                ? PriorityManager.Instance.GetCategoryPriority(TaskType.HaulResource)
                : 5,
            GridLayer.Structure);
        task.targetResourceDelivery = target;
        task.targetProductionMachine = target as ProductionMachineBehaviour;
        task.requestedResourceType = resourceType;
        taskRegistry.Add(task);
    }

    private bool HasDeliveryTask(
        IResourceDeliveryTarget target,
        ResourceType resourceType)
    {
        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            IResourceDeliveryTarget existing = task?.targetResourceDelivery
                ?? task?.targetProductionMachine;
            if (task != null
                && ReferenceEquals(existing, target)
                && task.type == TaskType.HaulResource
                && task.requestedResourceType == resourceType)
            {
                return true;
            }
        }

        return false;
    }

    public void AddMachineOperationTask(ProductionMachineBehaviour machine)
    {
        if (machine == null || HasMachineTask(machine, TaskType.OperateMachine, null))
        {
            return;
        }

        Task task = new Task(
            machine.GridPosition,
            TaskType.OperateMachine,
            TileType.Empty,
            null,
            PriorityManager.Instance != null
                ? PriorityManager.Instance.GetCategoryPriority(TaskType.OperateMachine)
                : 5,
            GridLayer.Structure);
        task.targetProductionMachine = machine;
        taskRegistry.Add(task);
    }

    private bool HasMachineTask(
        ProductionMachineBehaviour machine,
        TaskType type,
        ResourceType? resourceType)
    {
        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task task = pendingTasks[i];
            if (task != null
                && task.targetProductionMachine == machine
                && task.type == type
                && task.requestedResourceType == resourceType)
            {
                return true;
            }
        }

        return false;
    }

    public void RemoveTask(Task task)
    {
        taskRegistry.Remove(task);
    }

    public bool CancelTasksAt(Vector2Int position)
    {
        return taskCancellation.CancelTasksAt(
            pendingTasks,
            position,
            RemoveTask);
    }

    public int CancelTasksForMachine(ProductionMachineBehaviour machine)
    {
        if (machine == null) return 0;
        List<Task> matches = pendingTasks.FindAll(
            task => task != null && task.targetProductionMachine == machine);

        for (int i = 0; i < matches.Count; i++)
        {
            Task task = matches[i];
            taskCancellation.InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.ManualCancellation);
            task.isAssigned = false;
            RemoveTask(task);
        }

        return matches.Count;
    }

    public int CancelTasksForDeliveryTarget(IResourceDeliveryTarget target)
    {
        if (target == null) return 0;
        List<Task> matches = pendingTasks.FindAll(task =>
            task != null
            && ReferenceEquals(
                task.targetResourceDelivery ?? task.targetProductionMachine,
                target));

        for (int i = 0; i < matches.Count; i++)
        {
            Task task = matches[i];
            taskCancellation.InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.ManualCancellation);
            task.isAssigned = false;
            RemoveTask(task);
        }

        return matches.Count;
    }

    public int CancelTasksForBlueprint(
       ConstructionBlueprint blueprint)
    {
        return taskCancellation.CancelTasksForBlueprint(
            pendingTasks,
            blueprint,
            RemoveTask);
    }

    public void ReleaseTask(Task task)
    {
        taskRegistry.Release(task);
    }

    public Task GetTaskAt(Vector2Int gridPos)
    {
        return taskRegistry.GetAt(gridPos);
    }

    public Task GetTaskAt(
        Vector2Int gridPos,
        TaskType type,
        GridLayer targetLayer)
    {
        return taskRegistry.GetAt(
            gridPos,
            type,
            targetLayer);
    }

    public bool ContainsTask(Task task)
    {
        return taskRegistry.Contains(task);
    }

    public List<Task> GetTasksSnapshot()
    {
        return taskRegistry.GetSnapshot();
    }

    public void ClearTasksForLoad()
    {
        taskRegistry.ClearForLoad();

        taskSelector.Clear();
        batchSelector.Clear();
        taskCleanup.Reset();
        taskSearchBudget.Reset();
    }

    public bool TryAcquireTaskSearchSlot()
    {
        return taskSearchBudget.TryAcquire(
            maximumTaskSearchesPerFrame);
    }

    public Task GetNextTaskFor(
        Vector2Int dupeGridPos,
        out List<Vector2Int> calculatedPath,
        DuplicantCapabilityProfile profile = null,
        DuplicantWorkProfile workProfile = null,
        Predicate<Task> isTaskTemporarilyDeferred = null)
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
                    workProfile,
                    isTaskTemporarilyDeferred);
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
        DuplicantWorkProfile workProfile,
        Predicate<Task> isTaskTemporarilyDeferred)
    {
        RemoveInvalidTasks();

        selectionDiagnostics.Enabled =
            enableBuildTaskDiagnostics;

        return taskSelector.TrySelect(
            pendingTasks,
            dupeGridPos,
            profile,
            workProfile,
            handlerRegistry.Handlers,
            selectionDiagnostics.LogRejection,
            out calculatedPath,
            isTaskTemporarilyDeferred);
    }

    public bool TryAssignAdditionalBlueprintDeliveryTask(
        Vector2Int workerPosition,
        Vector2Int batchAnchor,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        out Task selectedTask,
        ISet<Task> excludedTasks = null,
        int maximumDistanceOverride = -1)
    {
        RemoveInvalidTasks();

        return batchSelector.TrySelectBlueprintDeliveryTask(
            pendingTasks,
            workerPosition,
            batchAnchor,
            resourceType,
            profile,
            maximumDistanceOverride > 0
                ? maximumDistanceOverride
                : MaximumBlueprintDeliveryDistance,
            maximumBatchPathChecks,
            out selectedTask,
            excludedTasks);
    }

    public bool TryAssignAdditionalGroundHaulTask(
        Vector2Int dupeGridPos,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        out Task selectedTask,
        out List<Vector2Int> calculatedPath)
    {
        RemoveInvalidTasks();

        return batchSelector.TrySelectGroundHaulTask(
            pendingTasks,
            dupeGridPos,
            resourceType,
            profile,
            Mathf.Max(1, maximumBatchPickupDistance),
            maximumBatchPathChecks,
            out selectedTask,
            out calculatedPath);
    }

    public bool TryAssignMachineSupplyTask(
        Vector2Int workerPosition,
        ResourceType resourceType,
        DuplicantCapabilityProfile profile,
        int maximumDistance,
        out Task selectedTask)
    {
        RemoveInvalidTasks();
        selectedTask = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < pendingTasks.Count; i++)
        {
            Task candidate = pendingTasks[i];
            if (candidate == null
                || candidate.isAssigned
                || candidate.type != TaskType.HaulResource
                || (candidate.targetResourceDelivery == null
                    && candidate.targetProductionMachine == null)
                || !candidate.requestedResourceType.HasValue
                || candidate.requestedResourceType.Value != resourceType
                || !IsTaskValid(candidate))
            {
                continue;
            }

            List<Vector2Int> path = TaskNavigationUtility.GetPathToTask(
                workerPosition,
                candidate,
                profile);
            if (path == null) continue;

            int distance = Mathf.Abs(workerPosition.x - candidate.gridPosition.x)
                + Mathf.Abs(workerPosition.y - candidate.gridPosition.y);
            if (distance > Mathf.Max(1, maximumDistance)) continue;
            int score = candidate.priority * 1000 - Mathf.Min(distance, 99);
            if (score <= bestScore) continue;

            bestScore = score;
            selectedTask = candidate;
        }

        if (selectedTask == null) return false;
        selectedTask.isAssigned = true;
        return true;
    }

    public int GetTaskSelectionScore(
        Task task,
        Vector2Int duplicantPosition,
        DuplicantWorkProfile workProfile = null)
    {
        return TaskSelectionService.CalculateScore(
            task,
            duplicantPosition,
            workProfile);
    }

    private void RemoveInvalidTasks(bool force = false)
    {
        RefreshGridSubscription();

        taskCleanup.Cleanup(
            pendingTasks,
            IsTaskValid,
            task => taskCancellation.InterruptAssignedTask(
                task,
                TaskInterruptionOrigin.TaskInvalidated,
                false,
                true),
            RemoveTask,
            force);
    }

    public bool IsTaskValid(Task task)
    {
        return taskValidator.IsValid(task);
    }
    public void ExecuteTask(
        Task task,
        DuplicantController dupe)
    {
        handlerRegistry.Execute(task, dupe);
    }

    public List<Vector2Int> GetPathToTask(
        Vector2Int startPos,
        Task task,
        DuplicantCapabilityProfile profile = null)
    {
        return TaskNavigationUtility.GetPathToTask(
            startPos,
            task,
            profile);
    }
}
