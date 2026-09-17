using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(
    typeof(DuplicantController),
    typeof(DuplicantMovement),
    typeof(DuplicantInventory))]
public class DuplicantTaskRunner : MonoBehaviour
{
    [Header("Roteamento de logística")]
    [SerializeField]
    private DuplicantLogisticsRoutingSettings logisticsRouting =
        new DuplicantLogisticsRoutingSettings();

    public bool HasCarriedInventory => inventory != null && inventory.HasItem;
    private DuplicantController controller;
    private DuplicantMovement movement;
    private DuplicantInventory inventory;
    private DuplicantPathFollower pathFollower;
    private DuplicantResourceCollector resourceCollector;
    private DuplicantMealExecutor mealExecutor;
    private DuplicantRestExecutor restExecutor;
    private readonly DuplicantStorageReservation storageReservation =
        new DuplicantStorageReservation();
    private Task activeTask;
    private readonly DuplicantGroundHaulState groundHaulState =
        new DuplicantGroundHaulState();
    private readonly DuplicantTaskDiagnostics diagnostics =
     new DuplicantTaskDiagnostics();
    private bool applicationIsQuitting;
    private bool isFinalizingTask;
    private readonly DuplicantBlueprintDeliveryPlan
        blueprintDeliveryPlan =
            new DuplicantBlueprintDeliveryPlan();
    private DuplicantWorkExecutor workExecutor;
    private DuplicantBlueprintDeliveryExecutor
    blueprintDeliveryExecutor;
    private DuplicantInventoryStorer inventoryStorer;
    private DuplicantTaskFinisher taskFinisher;
    private DuplicantGroundHaulExecutor
    groundHaulExecutor;
    private DuplicantCarriedResourceRouter carriedResourceRouter;
    public TaskFailureReason LastFailureReason =>
    diagnostics.LastFailureReason;
    public TaskInterruptionOrigin LastInterruptionOrigin =>
        diagnostics.LastInterruptionOrigin;
    public string LastFailureDetails =>
        diagnostics.LastFailureDetails;
    public float LastFailureTime =>
        diagnostics.LastFailureTime;
    public Vector2Int? DiagnosticDestination =>
        diagnostics.Destination;
    public int DiagnosticPathLength =>
        diagnostics.PathLength;
    public int DiagnosticPathIndex =>
        diagnostics.PathIndex;
    public int DiagnosticReplanCount =>
        diagnostics.ReplanCount;
    public int DiagnosticMaxReplans =>
        diagnostics.MaxReplans;
    public int LastMealConsumedPortions =>
        mealExecutor != null
            ? mealExecutor.LastConsumedPortions
            : 0;

    public string LastMealDetails =>
        mealExecutor != null
            ? mealExecutor.LastDetails
            : "Nenhuma refeição executada.";
    public BedRestResult LastBedRestResult => restExecutor != null
        ? restExecutor.Result
        : BedRestResult.None;
    public BedStructureBehaviour ActiveBed => restExecutor?.ActiveBed;

    public Task ActiveTask => activeTask;
    public bool HasStorageReservation =>
        storageReservation.IsActive;

    public Vector2Int ReservedStoragePosition =>
        storageReservation.StoragePosition;

    public ResourceType ReservedStorageType =>
        storageReservation.ResourceType;

    public int ReservedStorageAmount =>
        storageReservation.ReservedAmount;
    public bool HasResourceReservation =>
        blueprintDeliveryPlan.ReservedResourceAmount > 0;

    public ResourceType ReservedResourceType =>
        blueprintDeliveryPlan.ResourceType;

    public int ReservedResourceAmount =>
        blueprintDeliveryPlan.ReservedResourceAmount;

    public bool HasBlueprintReservation =>
        blueprintDeliveryPlan.ReservedDeliveryAmount > 0;

    public int ReservedDeliveryAmount =>
        blueprintDeliveryPlan.ReservedDeliveryAmount;

    public int ReservedBlueprintCount =>
        blueprintDeliveryPlan.Count;

    public bool CanContinueGroundHaul(Task task)
    {
        if (task == null
            || activeTask != task
            || !groundHaulState.HasCollectedItem
            || inventory == null
            || !inventory.HasItem
            || !inventory.CarriedType.HasValue
            || !HasStorageReservation
            || inventory.CarriedType.Value
    != storageReservation.ResourceType)
        {
            return false;
        }

        return storageReservation.IsValidFor(inventory);
    }

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
        movement = GetComponent<DuplicantMovement>();
        inventory = GetComponent<DuplicantInventory>();
        DuplicantVitals vitals = GetComponent<DuplicantVitals>();

        pathFollower = new DuplicantPathFollower(
            controller,
            movement,
            diagnostics);

        workExecutor = new DuplicantWorkExecutor(
            controller,
            movement,
            pathFollower);

        resourceCollector = new DuplicantResourceCollector(
            controller,
            inventory,
            pathFollower);

        inventoryStorer = new DuplicantInventoryStorer(
            controller,
            inventory,
            pathFollower,
            storageReservation);

        taskFinisher = new DuplicantTaskFinisher(
            controller,
            movement);

        blueprintDeliveryExecutor =
        new DuplicantBlueprintDeliveryExecutor(
            controller,
            inventory,
            pathFollower,
            resourceCollector,
            blueprintDeliveryPlan);

        carriedResourceRouter =
            new DuplicantCarriedResourceRouter(
                controller,
                inventory,
                blueprintDeliveryExecutor,
                storageReservation,
                logisticsRouting);

        groundHaulExecutor =
            new DuplicantGroundHaulExecutor(
                controller,
                inventory,
                pathFollower,
                storageReservation,
                groundHaulState,
                this,
                carriedResourceRouter,
                GetSafeDropPosition);

        mealExecutor = new DuplicantMealExecutor(
            controller,
            pathFollower);

        restExecutor = new DuplicantRestExecutor(
            controller,
            vitals,
            pathFollower);

    }

    private void OnDisable()
    {
        if (!SaveGameRuntime.IsLoading
            && !applicationIsQuitting
            && !isFinalizingTask
            && (activeTask != null || controller.currentTask != null))
        {
            CancelActiveTaskState(
                TaskInterruptionOrigin.ComponentDisabled);
        }
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
    }

    public IEnumerator ExecuteTaskRoutine(Task task, List<Vector2Int> path)
    {
        ReleaseDeliveryReservation();
        ReleaseStorageReservation();
        ReleaseAdditionalHaulTask();
        activeTask = task;
        groundHaulState.Clear(null);
        diagnostics.BeginTask();

        if (task == null)
        {
            FinishCurrentTask();
            yield break;
        }

        if (task.type == TaskType.HaulResource && task.targetBlueprint != null)
        {
            yield return StartCoroutine(ExecuteDeliveryTaskRoutine(task));
            yield break;
        }

        if (task.type == TaskType.HaulResource && task.targetItem != null)
        {
            yield return StartCoroutine(ExecuteGroundHaulRoutine(task, path));
            yield break;
        }

        if (task.type == TaskType.BuildTile && task.targetBlueprint != null)
        {
            yield return StartCoroutine(ExecuteAssemblyTaskRoutine(task, path));
            yield break;
        }

        yield return StartCoroutine(ExecuteGenericTaskRoutine(task, path));
    }

    public IEnumerator StoreCarriedInventoryRoutine()
    {
        yield return inventoryStorer.StoreCarriedInventory();

        switch (inventoryStorer.Result)
        {
            case InventoryStoreResult.Stored:
            case InventoryStoreResult.NothingToStore:
                controller.currentState =
                    DuplicantController.WorkerState.Idle;

                yield break;

            default:
                ReleaseStorageReservation();

                if (inventory != null && inventory.HasItem)
                {
                    inventory.DropCarriedItem(
                        GetSafeDropPosition());
                }

                controller.currentState =
                    DuplicantController.WorkerState.Idle;

                yield break;
        }
    }

    public IEnumerator ExecuteEatingRoutine(MealPlan mealPlan)
    {
        yield return mealExecutor.Execute(mealPlan);

    }

    public void CancelEmergencyFoodState()
    {
        mealExecutor?.Cancel();
    }

    public IEnumerator ExecuteBedRestRoutine()
    {
        yield return restExecutor.Execute();
    }

    public void CancelBedRestState()
    {
        restExecutor?.Cancel();
    }

    private IEnumerator ExecuteDeliveryTaskRoutine(Task task)
    {
        ConstructionBlueprint blueprint = task.targetBlueprint;

        // Mantém o comportamento anterior: não mistura recursos
        // de tipos diferentes no mesmo inventário.
        if (blueprint != null
            && inventory.HasItem
            && inventory.CarriedType != blueprint.requiredResource)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        yield return blueprintDeliveryExecutor.Execute(task);

        switch (blueprintDeliveryExecutor.Result)
        {
            case BlueprintDeliveryExecutionResult.Completed:
                FinishCurrentTask();
                yield break;

            case BlueprintDeliveryExecutionResult.PrimaryBlueprintInvalid:
                CancelTask(
                    task,
                    TaskFailureReason.TargetInvalid,
                    "O blueprint não está aguardando materiais.");
                yield break;

            case BlueprintDeliveryExecutionResult.NoMaterialPlan:
                DeferBlueprintDeliveryTask(
                    task,
                    "Sem material livre; procurando outra tarefa.");
                yield break;

            case BlueprintDeliveryExecutionResult.ResourceUnavailable:
                RecordFailure(
                    TaskFailureReason.ResourceUnavailable,
                    "O material reservado não estava alcançável para coleta.");
                DeferBlueprintDeliveryTask(
                    task,
                    "Entrega adiada; procurando outra tarefa executável.");
                yield break;

            case BlueprintDeliveryExecutionResult
                .NoDestinationReceivedLoad:

                RecordFailure(
                    TaskFailureReason.PathBlocked,
                    "Nenhum destino reservado pôde receber a carga.");

                FinishCurrentTask();
                yield break;

            default:
                CancelTask(
                    task,
                    TaskFailureReason.TargetInvalid,
                    "A entrega terminou em estado inválido.");
                yield break;
        }
    }
    private void DeferBlueprintDeliveryTask(
        Task task,
        string details)
    {
        if (task != null && task.targetBlueprint != null)
        {
            controller.Brain?.DeferBlueprintResourceSearch(
                task.targetBlueprint.requiredResource);
        }

        ReleaseAllReservations();
        TaskManager.Instance?.ReleaseTask(task);

        diagnostics.RecordInformation(details);

        FinishCurrentTask();
        controller.Brain?.RequestImmediateTaskSearch();
    }

    private IEnumerator ExecuteAssemblyTaskRoutine(
        Task task,
        List<Vector2Int> path)
    {
        yield return workExecutor.ExecuteAssembly(task, path);

        switch (workExecutor.Result)
        {
            case TaskWorkExecutionResult.Completed:
                FinishCurrentTask();
                yield break;

            case TaskWorkExecutionResult.TargetInvalid:
                CancelTask(
                    task,
                    TaskFailureReason.TargetInvalid,
                    "O blueprint não está pronto para montagem.");
                yield break;

            case TaskWorkExecutionResult.PathBlocked:
                CancelTask(
                    task,
                    TaskFailureReason.PathBlocked,
                    "Não foi possível chegar à construção.");
                yield break;

            case TaskWorkExecutionResult.TargetDestroyedBeforeWork:
                RemoveDestroyedTargetTask(
                    task,
                    "O blueprint foi destruído antes da montagem.");
                yield break;

            case TaskWorkExecutionResult.TargetDestroyedDuringWork:
                RemoveDestroyedTargetTask(
                    task,
                    "O blueprint foi destruído durante a montagem.");
                yield break;

            case TaskWorkExecutionResult.MovementFailed:
                CancelTask(
                    task,
                    TaskFailureReason.MovementFailed,
                    "O afastamento do tile da construção falhou.");
                yield break;

            default:
                CancelTask(
                    task,
                    TaskFailureReason.TargetInvalid,
                    "A execução da montagem terminou em estado inválido.");
                yield break;
        }
    }

    private IEnumerator ExecuteGroundHaulRoutine(
        Task task,
        List<Vector2Int> initialPath)
    {
        yield return groundHaulExecutor.Execute(
            task,
            initialPath);

        switch (groundHaulExecutor.Result)
        {
            case GroundHaulExecutionResult.Completed:
                FinishCurrentTask();
                yield break;

            case GroundHaulExecutionResult.InvalidTarget:
                RemoveInvalidTask(task);
                yield break;

            case GroundHaulExecutionResult.ItemReservedByOther:
                CancelTask(
                    task,
                    TaskFailureReason.ResourceUnavailable,
                    "Outro duplicant já está coletando este item.");
                yield break;

            case GroundHaulExecutionResult.StorageUnavailable:
                CancelTask(
                    task,
                    TaskFailureReason.StorageUnavailable,
                    "Não existe baú alcançável com espaço suficiente.");
                yield break;

            case GroundHaulExecutionResult.StorageReservationFailed:
                CancelTask(
                    task,
                    TaskFailureReason.ReservationFailed,
                    "Não foi possível registrar a reserva do baú.");
                yield break;

            case GroundHaulExecutionResult.ItemUnreachable:
                CancelTask(
                    task,
                    TaskFailureReason.TargetUnreachable,
                    "Não foi possível chegar ao item.");
                yield break;

            case GroundHaulExecutionResult.ItemUnavailable:
                CancelTask(
                    task,
                    TaskFailureReason.ResourceUnavailable,
                    "O item não estava mais disponível para coleta.");
                yield break;

            case GroundHaulExecutionResult.InventoryRejected:
                CancelTask(
                    task,
                    TaskFailureReason.ResourceUnavailable,
                    "O inventário recusou o item coletado.");
                yield break;

            case GroundHaulExecutionResult
                .StorageUnreachableAfterPickup:

                FailGroundHaulAfterPickup(
                    task,
                    TaskFailureReason.StorageUnreachable,
                    "O baú ficou inalcançável antes da entrega.");
                yield break;

            case GroundHaulExecutionResult.StorageRejected:
                FailGroundHaulAfterPickup(
                    task,
                    TaskFailureReason.StorageUnavailable,
                    "O baú recusou o item reservado.");
                yield break;

            default:
                RemoveInvalidTask(task);
                yield break;
        }
    }

    private IEnumerator ExecuteGenericTaskRoutine(
        Task task,
        List<Vector2Int> path)
    {
        yield return workExecutor.ExecuteGeneric(task, path);

        switch (workExecutor.Result)
        {
            case TaskWorkExecutionResult.Completed:
                FinishCurrentTask();
                yield break;

            case TaskWorkExecutionResult.PathBlocked:
                CancelTask(
                    task,
                    TaskFailureReason.PathBlocked,
                    "Não foi possível chegar ao destino da tarefa.");
                yield break;

            case TaskWorkExecutionResult.TargetDestroyedBeforeWork:
                RemoveDestroyedTargetTask(
                    task,
                    "O alvo deixou de ser válido antes do trabalho.");
                yield break;

            case TaskWorkExecutionResult.TargetDestroyedDuringWork:
                RemoveDestroyedTargetTask(
                    task,
                    "O alvo mudou durante o trabalho.");
                yield break;

            default:
                RemoveDestroyedTargetTask(
                    task,
                    "A execução da tarefa terminou em estado inválido.");
                yield break;
        }
    }

    public void CancelActiveTaskState(bool preserveCarriedInventory = false)
    {
        CancelActiveTaskState(
            TaskInterruptionOrigin.ExternalSystem,
            preserveCarriedInventory);
    }

    public void CancelActiveTaskState(
        TaskInterruptionOrigin origin,
        bool preserveCarriedInventory = false)
    {
        if (isFinalizingTask) return;
        isFinalizingTask = true;

        if (!preserveCarriedInventory
            && (activeTask != null || controller.currentTask != null))
        {
            diagnostics.SetInterruptionOrigin(origin);

            RecordFailure(
                TaskFailureReason.Interrupted,
                BuildInterruptionDetails(origin)
            );
        }

        StopAllCoroutines();
        ReleaseAllReservations();

        if (!preserveCarriedInventory
            && inventory != null && inventory.HasItem)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        if (groundHaulState.HasCollectedItem)
        {
            TaskManager.Instance?.RemoveTask(activeTask);
            TryCreateRemainingGroundHaulTask();
        }
        else
        {
            TaskManager.Instance?.ReleaseTask(activeTask);
        }

        ClearGroundHaulTracking();
        controller.currentTask = null;
        controller.currentState = DuplicantController.WorkerState.Idle;
        isFinalizingTask = false;
    }

    private string BuildInterruptionDetails(TaskInterruptionOrigin origin)
    {
        Vector2Int logicalPosition = controller != null
            ? controller.gridPosition
            : Vector2Int.zero;
        Vector2Int visualPosition = GridManager.Instance != null
            ? GridManager.Instance.WorldToGridPosition(transform.position)
            : logicalPosition;
        string taskDescription = activeTask != null
            ? $"{activeTask.type} em {activeTask.gridPosition} "
                + $"(Haul={activeTask.groundHaulPhase})"
            : "nenhuma";
        string inventoryDescription = inventory != null && inventory.HasItem
            ? $"{inventory.CarriedAmount}x {inventory.CarriedType}"
            : "vazio";
        string storageDescription = storageReservation.IsActive
            ? $"{storageReservation.ReservedAmount}x "
                + $"{storageReservation.ResourceType} em "
                + $"{storageReservation.StoragePosition}"
            : "nenhuma";

        return $"Origem={origin}; Tarefa={taskDescription}; "
            + $"Grid={logicalPosition}; Visual={visualPosition}; "
            + $"Inventário={inventoryDescription}; ReservaBaú={storageDescription}.";
    }

    private void ReleaseAllReservations()
    {
        ReleaseDeliveryReservation();
        ReleaseStorageReservation();
        ReleaseGroundItemReservation();
        ReleaseAdditionalHaulTask();
        ReleaseFoodReservation();
    }

    private void ReleaseFoodReservation()
    {
        mealExecutor?.Cancel();
    }

    private Vector3 GetSafeDropPosition()
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager == null) return transform.position;

        Vector2Int currentPosition =
            gridManager.WorldToGridPosition(transform.position);

        Vector2Int safePosition =
            gridManager.IsStandable(currentPosition.x, currentPosition.y)
                ? currentPosition
                : GridSafetyUtility.FindNearestStandableTileBFS(
                    currentPosition
                );

        return gridManager.GridToWorldPosition(safePosition);
    }

    private void ReleaseDeliveryReservation()
    {
        blueprintDeliveryPlan.ReleaseAll();
    }

    private void ReleaseStorageReservation()
    {
        storageReservation.Release();
    }

    private void ReleaseGroundItemReservation()
    {
        groundHaulState.ReleaseItemReservation(this);
    }

    private void ReleaseAdditionalHaulTask(
        bool removeTask = false)
    {
        groundHaulState.ReleaseAdditionalTask(removeTask);
    }

    private void FailGroundHaulAfterPickup(
        Task task,
        TaskFailureReason reason = TaskFailureReason.StorageUnreachable,
        string details = "Não foi possível concluir a entrega no baú.")
    {
        RecordFailure(reason, details);
        ReleaseStorageReservation();
        ReleaseGroundItemReservation();
        TaskManager.Instance?.RemoveTask(task);

        if (inventory.HasItem)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        TryCreateRemainingGroundHaulTask();
        FinishCurrentTask();
    }

    private void RemoveInvalidTask(Task task)
    {
        RecordFailure(
            TaskFailureReason.TargetDestroyed,
            "O item alvo não existe mais."
        );
        ReleaseStorageReservation();
        ReleaseGroundItemReservation();
        TaskManager.Instance?.RemoveTask(task);
        FinishCurrentTask();
    }

    private void RemoveDestroyedTargetTask(Task task, string details)
    {
        RecordFailure(TaskFailureReason.TargetDestroyed, details);
        ReleaseAllReservations();
        TaskManager.Instance?.RemoveTask(task);
        FinishCurrentTask();
    }

    private void TryCreateRemainingGroundHaulTask()
    {
        ResourceItem item = groundHaulState.ActiveItem;

        if (item != null
            && item.gameObject.activeInHierarchy
            && item.amount > 0)
        {
            TaskManager.Instance?.AddHaulTask(item);
        }
    }

    private void ClearGroundHaulTracking()
    {
        groundHaulState.Clear(activeTask);
        activeTask = null;
    }

    private void CancelTask(
        Task task,
        TaskFailureReason reason = TaskFailureReason.Interrupted,
        string details = "A tarefa foi interrompida.")
    {
        if (!diagnostics.HasFailureForActiveTask)
        {
            RecordFailure(reason, details);
        }
        ReleaseAllReservations();
        TaskManager.Instance?.ReleaseTask(task);
        controller.Brain?.SetSearchCooldown(2.5f);
        FinishCurrentTask();
    }

    public void ClearFailureDiagnostics()
    {
        diagnostics.ClearFailure();
    }
    private void RecordFailure(
        TaskFailureReason reason,
        string details)
    {
        diagnostics.RecordFailure(reason, details);
    }

    private void FinishCurrentTask()
    {
        taskFinisher.Finish(
            ReleaseAllReservations,
            ClearGroundHaulTracking);
    }
}
