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
    private IFoodSource reservedFoodSource;
private readonly DuplicantBlueprintDeliveryPlan
    blueprintDeliveryPlan =
        new DuplicantBlueprintDeliveryPlan();
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
    public int LastMealConsumedPortions { get; private set; }
    public string LastMealDetails { get; private set; } = "Nenhuma refeição executada.";

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

        resourceCollector = new DuplicantResourceCollector(
            controller,
            inventory,
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
        LastMealConsumedPortions = 0;
        LastMealDetails = "Plano de refeição inválido.";
        reservedFoodSource = mealPlan != null ? mealPlan.Source : null;

        if (mealPlan == null
            || !IsFoodSourceValid(reservedFoodSource)
            || reservedFoodSource.GetReservedPortionCount(controller) <= 0)
        {
            ReleaseFoodReservation();
            yield break;
        }

        yield return StartCoroutine(
            FollowPathToInteractionPosition(
                reservedFoodSource.GridPosition,
                mealPlan.Path,
                mealPlan.InteractionPosition)
        );

        if (!pathFollowSucceeded)
        {
            LastMealDetails = "A posição reservada do baú ficou inalcançável.";
            ReleaseFoodReservation();
            controller.currentState = DuplicantController.WorkerState.Idle;
            yield break;
        }

        if (!IsFoodSourceValid(reservedFoodSource))
        {
            LastMealDetails = "A fonte de alimento foi destruída ou desativada.";
            ReleaseFoodReservation();
            controller.currentState = DuplicantController.WorkerState.Idle;
            yield break;
        }

        if (reservedFoodSource.GetReservedPortionCount(controller) <= 0)
        {
            LastMealDetails = "A reserva alimentar foi liberada antes do consumo.";
            ReleaseFoodReservation();
            controller.currentState = DuplicantController.WorkerState.Idle;
            yield break;
        }

        controller.currentState = DuplicantController.WorkerState.Eating;
        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;

        int consumedPortions = 0;
        float totalRestored = 0f;
        while (controller.Vitals != null
            && controller.Vitals.CurrentHunger < mealPlan.HungerTarget
            && IsFoodSourceValid(reservedFoodSource)
            && reservedFoodSource.GetReservedPortionCount(controller) > 0)
        {
            yield return new WaitForSeconds(
                settings != null
                    ? settings.foodEatingDurationPerPortion
                    : 1.2f);

            IFoodSource food = reservedFoodSource;
            if (!IsFoodSourceValid(food)
                || !food.TryConsumeReservedPortion(
                    controller,
                    out float hungerRestored,
                    out bool isRawFood))
            {
                LastMealDetails = IsFoodSourceValid(food)
                    ? "A fonte recusou uma porção que ainda constava como reservada."
                    : "A fonte foi invalidada durante o consumo.";
                break;
            }

            controller.Vitals.RestoreHunger(hungerRestored);
            ApplyRawFoodEffectIfNeeded(isRawFood, settings);
            totalRestored += hungerRestored;
            consumedPortions++;
        }

        ReleaseFoodReservation();
        if (consumedPortions > 0)
        {
            LastMealConsumedPortions = consumedPortions;
            LastMealDetails = $"Consumiu {consumedPortions} porção(ões).";
            GameEvents.TriggerFloatingTextRequested(
                $"Refeição: {consumedPortions}x (+{totalRestored:F0})",
                transform.position,
                Color.green);
        }
        else if (LastMealDetails == "Plano de refeição inválido.")
        {
            LastMealDetails = "Nenhuma porção reservada pôde ser consumida.";
        }

        controller.currentState = DuplicantController.WorkerState.Idle;
    }

    private static bool IsFoodSourceValid(IFoodSource source)
    {
        return source != null
            && (!(source is Object unityObject) || unityObject != null);
    }

    private void ApplyRawFoodEffectIfNeeded(
        bool isRawFood,
        LifeCycleSettingsSO settings)
    {
        if (!isRawFood || settings == null || controller.StatusEffects == null
            || Random.value > settings.rawFoodDiscomfortChance)
        {
            return;
        }

        controller.StatusEffects.ApplyRawFoodDiscomfort(
            settings.rawFoodDiscomfortDuration,
            settings.rawFoodWorkPenaltyPercent);

        GameEvents.TriggerFloatingTextRequested(
            "Desconforto por alimento cru",
            transform.position,
            Color.yellow);
    }

    public void CancelEmergencyFoodState()
    {
        ReleaseFoodReservation();
    }

    private IEnumerator ExecuteDeliveryTaskRoutine(Task task)
    {
        ConstructionBlueprint primaryBlueprint = task.targetBlueprint;
        if (primaryBlueprint == null
            || primaryBlueprint.CurrentState
                != BlueprintState.WaitingMaterials)
        {
            CancelTask(task, TaskFailureReason.TargetInvalid,
                "O blueprint não está aguardando materiais.");
            yield break;
        }

        ResourceType requiredResource = primaryBlueprint.requiredResource;

        if (inventory.HasItem
            && inventory.CarriedType != requiredResource)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        if (!TryBuildBlueprintDeliveryPlan(
                task,
                primaryBlueprint,
                requiredResource))
        {
            DeferBlueprintDeliveryTask(
                task,
                "Sem material livre; procurando outra tarefa.");
            yield break;
        }

        int plannedAmount =
    blueprintDeliveryPlan.ReservedDeliveryAmount;
        yield return StartCoroutine(
            FetchResourceRoutine(requiredResource, plannedAmount));

        if (!inventory.HasItem)
        {
            CancelTask(
                task,
                TaskFailureReason.ResourceUnavailable,
                "O material reservado não estava alcançável para coleta.");
            yield break;
        }

        TrimBlueprintDeliveryPlanToCollectedAmount(
            inventory.CarriedAmount);

        int deliveredTotal = 0;

        for (int i = 0;
             i < blueprintDeliveryPlan.Count && inventory.HasItem;
             i++)
        {
            BlueprintDeliveryStop stop =
                blueprintDeliveryPlan.GetStop(i);

            if (stop.ReservedAmount <= 0)
            {
                continue;
            }

            ConstructionBlueprint blueprint = stop.Blueprint;
            if (blueprint == null
                || blueprint.CurrentState
                    != BlueprintState.WaitingMaterials)
            {
                ReleaseBlueprintDeliveryStop(stop);
                continue;
            }

            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToTask(
                    controller.gridPosition,
                    stop.Task,
                    controller.capabilityProfile);

            if (path == null)
            {
                ReleaseBlueprintDeliveryStop(stop);
                continue;
            }

            yield return StartCoroutine(
                FollowTaskPath(stop.Task, path));

            if (!pathFollowSucceeded || blueprint == null)
            {
                ReleaseBlueprintDeliveryStop(stop);
                continue;
            }

            int requestedAmount = Mathf.Min(
                stop.ReservedAmount,
                inventory.CarriedAmount);

            int deliveredAmount = blueprint.DeliverResource(
                requiredResource,
                requestedAmount);

            if (deliveredAmount <= 0)
            {
                ReleaseBlueprintDeliveryStop(stop);
                continue;
            }

            int confirmedAmount =
                blueprintDeliveryPlan.ConfirmDelivery(
                    stop,
                    deliveredAmount);

            inventory.RemoveItem(
                requiredResource,
                confirmedAmount);

            deliveredTotal += deliveredAmount;

            if (stop.ReservedAmount > 0)
            {
                ReleaseBlueprintDeliveryStop(stop);
            }

            TaskManager.Instance?.RemoveTask(stop.Task);
            blueprint.NotifyTaskEnded(stop.Task);
        }

        if (deliveredTotal <= 0)
        {
            RecordFailure(
                TaskFailureReason.PathBlocked,
                "Nenhum destino reservado pôde receber a carga.");
        }

        FinishCurrentTask();
    }

    private bool TryBuildBlueprintDeliveryPlan(
        Task primaryTask,
        ConstructionBlueprint primaryBlueprint,
        ResourceType resourceType)
    {
        if (StockpileManager.Instance == null
            || TaskManager.Instance == null)
        {
            return false;
        }

        int capacityRemaining = inventory.SpaceRemaining;

        if (capacityRemaining <= 0)
        {
            return false;
        }

        if (!TryAddBlueprintDeliveryStop(
                primaryTask,
                primaryBlueprint,
                capacityRemaining,
                resourceType))
        {
            return false;
        }

        capacityRemaining = inventory.SpaceRemaining
            - blueprintDeliveryPlan.ReservedDeliveryAmount;

        Vector2Int batchAnchor = primaryBlueprint.gridPosition;

        int maximumStops =
            TaskManager.Instance.MaximumBlueprintDeliveriesPerRun;

        while (capacityRemaining > 0
               && blueprintDeliveryPlan.Count < maximumStops
               && StockpileManager.Instance.GetAvailableAmount(
                   resourceType) > 0)
        {
            if (!TaskManager.Instance
                    .TryAssignAdditionalBlueprintDeliveryTask(
                        controller.gridPosition,
                        batchAnchor,
                        resourceType,
                        controller.capabilityProfile,
                        out Task additionalTask))
            {
                break;
            }

            if (!TryAddBlueprintDeliveryStop(
                    additionalTask,
                    additionalTask.targetBlueprint,
                    capacityRemaining,
                    resourceType))
            {
                TaskManager.Instance.ReleaseTask(additionalTask);
                break;
            }

            capacityRemaining = inventory.SpaceRemaining
                - blueprintDeliveryPlan.ReservedDeliveryAmount;
        }

        return blueprintDeliveryPlan.ReservedDeliveryAmount > 0;
    }

    private bool TryAddBlueprintDeliveryStop(
        Task task,
        ConstructionBlueprint blueprint,
        int capacityRemaining,
        ResourceType resourceType)
    {
        return blueprintDeliveryPlan.TryAddStop(
            task,
            blueprint,
            capacityRemaining,
            resourceType);
    }

    private void TrimBlueprintDeliveryPlanToCollectedAmount(
        int collectedAmount)
    {
        blueprintDeliveryPlan.TrimToCollectedAmount(
            collectedAmount);
    }

    private void ReleaseBlueprintDeliveryStop(
        BlueprintDeliveryStop stop)
    {
        blueprintDeliveryPlan.ReleaseStop(stop);
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

    private IEnumerator ExecuteAssemblyTaskRoutine(Task task, List<Vector2Int> path)
    {
        ConstructionBlueprint bp = task.targetBlueprint;
        if (bp == null || bp.CurrentState != BlueprintState.ReadyToBuild)
        {
            CancelTask(task, TaskFailureReason.TargetInvalid,
                "O blueprint não está pronto para montagem.");
            yield break;
        }

        Vector2Int targetBuildPos = bp.gridPosition;

        yield return StartCoroutine(FollowTaskPath(task, path));

        if (!pathFollowSucceeded)
        {
            CancelTask(task, TaskFailureReason.PathBlocked,
                "Não foi possível chegar à construção.");
            yield break;
        }

        if (bp == null)
        {
            RemoveDestroyedTargetTask(task, "O blueprint foi destruído antes da montagem.");
            yield break;
        }

        if (bp != null && controller.gridPosition == targetBuildPos && bp.targetTileType != TileType.Ladder)
        {
            Vector2Int safeNeighbor = GridSafetyUtility.FindNearestStandableTileBFS(controller.gridPosition);
            if (safeNeighbor != controller.gridPosition)
            {
                MovementType moveType = movement.DeduceMovementType(controller.gridPosition, safeNeighbor);
                yield return movement.MoveToTile(safeNeighbor, moveType, movement.GetCurrentMoveSpeed(moveType));

                if (!movement.LastMoveSucceeded)
                {
                    CancelTask(task, TaskFailureReason.MovementFailed,
                        "O afastamento do tile da construção falhou.");
                    yield break;
                }
            }
        }

        controller.currentState = DuplicantController.WorkerState.Working;

        bool isCompleted = false;
        while (!isCompleted && bp != null)
        {
            float workDelta = Time.deltaTime
                * controller.WorkEfficiencyMultiplier;
            isCompleted = bp.ApplyWork(workDelta);
            yield return null;
        }

        if (bp == null && !isCompleted)
        {
            RemoveDestroyedTargetTask(task, "O blueprint foi destruído durante a montagem.");
            yield break;
        }

        FinishCurrentTask();
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

    private IEnumerator ExecuteGenericTaskRoutine(Task task, List<Vector2Int> path)
    {
        yield return StartCoroutine(FollowTaskPath(task, path));

        if (!pathFollowSucceeded)
        {
            CancelTask(task, TaskFailureReason.PathBlocked,
                "Não foi possível chegar ao destino da tarefa.");
            yield break;
        }

        if (TaskManager.Instance == null
            || !TaskManager.Instance.IsTaskValid(task))
        {
            RemoveDestroyedTargetTask(task, "O alvo deixou de ser válido antes do trabalho.");
            yield break;
        }

        controller.currentState = DuplicantController.WorkerState.Working;
        yield return new WaitForSeconds(
            controller.workDuration
            / Mathf.Max(0.05f, controller.WorkEfficiencyMultiplier));

        if (!TaskManager.Instance.IsTaskValid(task))
        {
            RemoveDestroyedTargetTask(task, "O alvo mudou durante o trabalho.");
            yield break;
        }

        TaskManager.Instance?.ExecuteTask(task, controller);
        TaskManager.Instance?.RemoveTask(task);
        FinishCurrentTask();
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
        if (reservedFoodSource != null)
        {
            reservedFoodSource.ReleaseReservation(controller);
            reservedFoodSource = null;
        }
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