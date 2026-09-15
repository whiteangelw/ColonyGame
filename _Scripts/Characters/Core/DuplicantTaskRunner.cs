using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(
    typeof(DuplicantController),
    typeof(DuplicantMovement),
    typeof(DuplicantInventory))]
public class DuplicantTaskRunner : MonoBehaviour
{
    public bool HasCarriedInventory => inventory != null && inventory.HasItem;
    private DuplicantController controller;
    private DuplicantMovement movement;
    private DuplicantInventory inventory;
    private DuplicantPathFollower pathFollower;
    private DuplicantResourceCollector resourceCollector;
    private DuplicantMealExecutor mealExecutor;
    private bool pathFollowSucceeded;
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

        blueprintDeliveryExecutor =
        new DuplicantBlueprintDeliveryExecutor(
            controller,
            inventory,
            pathFollower,
            resourceCollector,
            blueprintDeliveryPlan);

        mealExecutor = new DuplicantMealExecutor(
            controller,
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
        if (inventory == null || !inventory.HasItem
            || !inventory.CarriedType.HasValue)
        {
            yield break;
        }

        ResourceType type = inventory.CarriedType.Value;
        int amount = inventory.CarriedAmount;

        if (StructureManager.Instance == null
            || !StructureManager.Instance.TryReserveReachableStorage(
                type,
                amount,
                controller.gridPosition,
                out IStorage storage))
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
            controller.currentState = DuplicantController.WorkerState.Idle;
            yield break;
        }

        if (!storageReservation.Track(
                storage,
                type,
                amount))
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
            controller.currentState =
                DuplicantController.WorkerState.Idle;

            yield break;
        }

        List<Vector2Int> path =
            TaskNavigationUtility.GetPathToInteractionPosition(
                controller.gridPosition,
                storageReservation.StoragePosition,
                true,
                controller.capabilityProfile);

        pathFollowSucceeded = false;
        if (path != null)
        {
            yield return StartCoroutine(FollowPathToInteractionPosition(
                storageReservation.StoragePosition,
                path));
        }

        if (pathFollowSucceeded && TryStoreReservedInventory())
        {
            ClearStorageReservationTracking();
            inventory.Clear();
        }
        else
        {
            ReleaseStorageReservation();
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        controller.currentState = DuplicantController.WorkerState.Idle;
    }

    public IEnumerator ExecuteEatingRoutine(MealPlan mealPlan)
    {
        yield return mealExecutor.Execute(mealPlan);

        // Mantém o estado usado pelo runner consistente.
        pathFollowSucceeded = pathFollower.Succeeded;
    }

    public void CancelEmergencyFoodState()
    {
        mealExecutor?.Cancel();
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

        pathFollowSucceeded = pathFollower.Succeeded;

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
                CancelTask(
                    task,
                    TaskFailureReason.ResourceUnavailable,
                    "O material reservado não estava alcançável "
                        + "para coleta.");
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

        pathFollowSucceeded = pathFollower.Succeeded;

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

    private IEnumerator FetchResourceRoutine(
        ResourceType type,
        int targetAmount)
    {
        yield return resourceCollector.Fetch(
            type,
            targetAmount);

        // Mantém a compatibilidade com o restante do runner.
        pathFollowSucceeded = pathFollower.Succeeded;
    }

    private IEnumerator ExecuteGroundHaulRoutine(
        Task task,
        List<Vector2Int> initialPath)
    {
        ResourceItem item = task.targetItem;
        groundHaulState.SetActiveItem(item);

        if (item == null
            || !item.gameObject.activeInHierarchy
            || !item.IsReadyForHaul
            || item.amount <= 0
            || StructureManager.Instance == null)
        {
            RemoveInvalidTask(task);
            yield break;
        }

        if (!groundHaulState.TryReserveItem(item, this))
        {
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "Outro duplicant já está coletando este item.");
            yield break;
        }

        if (inventory.HasItem && inventory.CarriedType != item.type)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        int pickupAmount = Mathf.Min(
            item.amount,
            inventory.SpaceRemaining
        );

        int amountToStore = inventory.CarriedAmount + pickupAmount;

        if (amountToStore <= 0
            || !StructureManager.Instance.TryReserveReachableStorage(
                item.type,
                amountToStore,
                GridManager.Instance.WorldToGridPosition(
                    item.transform.position
                ),
                out IStorage storage))
        {
            CancelTask(task, TaskFailureReason.StorageUnavailable,
                "Não existe baú alcançável com espaço suficiente.");
            yield break;
        }

        if (!storageReservation.Track(
                storage,
                item.type,
                amountToStore))
        {
            CancelTask(
                task,
                TaskFailureReason.ReservationFailed,
                "Não foi possível registrar a reserva do baú.");

            yield break;
        }

        Vector2Int itemPosition =
            GridManager.Instance.WorldToGridPosition(item.transform.position);

        List<Vector2Int> pathToItem = initialPath;

        if (pathToItem == null || task.gridPosition != itemPosition)
        {
            pathToItem = PathfindingAStar.Instance?.FindPath(
                controller.gridPosition,
                itemPosition,
                controller.capabilityProfile
            );

            task.gridPosition = itemPosition;
        }

        yield return StartCoroutine(
            FollowPathToPosition(itemPosition, pathToItem)
        );

        if (!pathFollowSucceeded
            || item == null
            || !item.gameObject.activeInHierarchy)
        {
            ReleaseStorageReservation();
            CancelTask(task, TaskFailureReason.TargetUnreachable,
                "Não foi possível chegar ao item.");
            yield break;
        }

        int requestedPickup = Mathf.Min(
            pickupAmount,
            inventory.SpaceRemaining
        );

        if (!item.TryTake(requestedPickup, out int takenAmount))
        {
            ReleaseGroundItemReservation();
            ReleaseStorageReservation();
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "O item não estava mais disponível para coleta.");
            yield break;
        }

        ReleaseGroundItemReservation();

        int acceptedPickup = inventory.AddItem(item.type, takenAmount);

        if (acceptedPickup < takenAmount)
        {
            int rejectedAmount = takenAmount - acceptedPickup;
            item.ReturnAmount(rejectedAmount);
        }

        if (acceptedPickup <= 0)
        {
            ReleaseStorageReservation();
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "O inventário recusou o item coletado.");
            yield break;
        }

        groundHaulState.MarkItemCollected();
        task.MarkGroundHaulCarrying(this);

        int unusedReservation =
           storageReservation.ReservedAmount
           - inventory.CarriedAmount;

        if (unusedReservation > 0)
        {
            storageReservation.ReleaseAmount(
                unusedReservation);
        }

        if (item.amount <= 0)
        {
            item.Recycle();
        }

        yield return StartCoroutine(CollectAdditionalGroundItems(item.type));

        List<Vector2Int> pathToStorage =
            TaskNavigationUtility.GetPathToInteractionPosition(
                controller.gridPosition,
                storageReservation.StoragePosition,
                true,
                controller.capabilityProfile
            );

        if (pathToStorage == null)
        {
            FailGroundHaulAfterPickup(
                task,
                TaskFailureReason.StorageUnreachable,
                "O baú ficou inalcançável antes da entrega."
            );
            yield break;
        }

        yield return StartCoroutine(
            FollowPathToInteractionPosition(
                storageReservation.StoragePosition,
                pathToStorage
            )
        );

        if (!pathFollowSucceeded
            || !TryStoreReservedInventory())
        {
            FailGroundHaulAfterPickup(
                task,
                pathFollowSucceeded
                    ? TaskFailureReason.StorageUnavailable
                    : TaskFailureReason.StorageUnreachable,
                pathFollowSucceeded
                    ? "O baú recusou o item reservado."
                    : "O caminho até o baú foi bloqueado."
            );
            yield break;
        }

        ClearStorageReservationTracking();
        inventory.Clear();

        TaskManager.Instance?.RemoveTask(task);

        if (item != null
            && item.gameObject.activeInHierarchy
            && item.amount > 0)
        {
            TaskManager.Instance?.AddHaulTask(item);
        }

        FinishCurrentTask();
    }

    private IEnumerator ExecuteGenericTaskRoutine(
        Task task,
        List<Vector2Int> path)
    {
        yield return workExecutor.ExecuteGeneric(task, path);

        pathFollowSucceeded = pathFollower.Succeeded;

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

    private IEnumerator CollectAdditionalGroundItems(ResourceType type)
    {
        while (inventory.SpaceRemaining > 0)
        {
            if (TaskManager.Instance == null
                || !TaskManager.Instance.TryAssignAdditionalGroundHaulTask(
                    controller.gridPosition,
                    type,
                    controller.capabilityProfile,
                    out Task additionalTask,
                    out List<Vector2Int> path))
            {
                yield break;
            }

            ResourceItem item = additionalTask.targetItem;
            groundHaulState.TrackAdditionalTask(additionalTask);

            if (item == null
                || !item.gameObject.activeInHierarchy
                || !item.IsReadyForHaul
                || item.type != type
                || item.amount <= 0)
            {
                ReleaseAdditionalHaulTask(true);
                continue;
            }

            if (!groundHaulState.TryReserveItem(item, this))
            {
                ReleaseAdditionalHaulTask();
                yield break;
            }

            Vector2Int itemPosition = GridManager.Instance.WorldToGridPosition(
                item.transform.position
            );

            yield return StartCoroutine(FollowPathToPosition(itemPosition, path));

            if (!pathFollowSucceeded || item == null
                || !item.gameObject.activeInHierarchy)
            {
                ReleaseGroundItemReservation();
                ReleaseAdditionalHaulTask();
                yield break;
            }

            int amountToTake = Mathf.Min(item.amount, inventory.SpaceRemaining);
            if (!TryExpandStorageReservation(amountToTake))
            {
                ReleaseGroundItemReservation();
                ReleaseAdditionalHaulTask();
                yield break;
            }

            if (!item.TryTake(amountToTake, out int takenAmount))
            {
                ReleaseStorageReservationAmount(amountToTake);
                ReleaseGroundItemReservation();
                ReleaseAdditionalHaulTask();
                yield break;
            }

            int acceptedAmount = inventory.AddItem(type, takenAmount);
            int rejectedAmount = takenAmount - acceptedAmount;

            if (rejectedAmount > 0)
            {
                item.ReturnAmount(rejectedAmount);
                ReleaseStorageReservationAmount(rejectedAmount);
            }

            ReleaseGroundItemReservation();

            if (item.amount <= 0)
            {
                item.Recycle();
                ReleaseAdditionalHaulTask(true);
            }
            else
            {
                ReleaseAdditionalHaulTask();
            }
        }
    }

    private IEnumerator FollowTaskPath(
        Task task,
        List<Vector2Int> initialPath)
    {
        pathFollowSucceeded = false;

        yield return pathFollower.FollowTask(
            task,
            initialPath);

        pathFollowSucceeded = pathFollower.Succeeded;
    }

    private IEnumerator FollowPathToPosition(
        Vector2Int targetPosition,
        List<Vector2Int> initialPath)
    {
        pathFollowSucceeded = false;

        yield return pathFollower.FollowPosition(
            targetPosition,
            initialPath);

        pathFollowSucceeded = pathFollower.Succeeded;
    }

    private IEnumerator FollowPathToInteractionPosition(
        Vector2Int targetPosition,
        List<Vector2Int> initialPath,
        Vector2Int? fixedInteractionPosition = null)
    {
        pathFollowSucceeded = false;

        yield return pathFollower.FollowInteraction(
            targetPosition,
            initialPath,
            fixedInteractionPosition);

        pathFollowSucceeded = pathFollower.Succeeded;
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

    private void ClearStorageReservationTracking()
    {
        storageReservation.Complete();
    }

    private bool TryStoreReservedInventory()
    {
        return storageReservation.TryStore(inventory);
    }

    private bool TryExpandStorageReservation(int amount)
    {
        return storageReservation.TryExpand(amount);
    }

    private void ReleaseStorageReservationAmount(int amount)
    {
        storageReservation.ReleaseAmount(amount);
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

    private void AbortDeliveryTask(
        Task task,
        TaskFailureReason reason,
        string details)
    {
        RecordFailure(reason, details);
        ReleaseAllReservations();

        if (inventory.HasItem)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        TaskManager.Instance?.RemoveTask(task);
        task.targetBlueprint?.NotifyTaskEnded(task);
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

    private void BeginPathDiagnostics(
        Vector2Int destination,
        List<Vector2Int> path,
        int maxReplans)
    {
        diagnostics.BeginPath(
            destination,
            path,
            maxReplans
        );
    }

    private void UpdatePathDiagnostics(
        List<Vector2Int> path,
        int pathIndex,
        int replanCount)
    {
        diagnostics.UpdatePath(
            path,
            pathIndex,
            replanCount
        );
    }

    private void ResetPathDiagnostics()
    {
        diagnostics.ResetPath();
    }

    private void FinishCurrentTask()
    {
        ReleaseAllReservations();
        ClearGroundHaulTracking();
        controller.currentTask = null;
        controller.currentState = DuplicantController.WorkerState.Idle;

        if (movement.ShouldFall())
        {
            StartCoroutine(movement.HandleFallingRoutine());
        }
    }
}