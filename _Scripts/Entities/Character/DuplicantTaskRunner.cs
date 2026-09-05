using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(
    typeof(DuplicantController),
    typeof(DuplicantMovement),
    typeof(DuplicantInventory))]
public class DuplicantTaskRunner : MonoBehaviour
{
    private DuplicantController controller;
    private DuplicantMovement movement;
    private DuplicantInventory inventory;

    private bool pathFollowSucceeded;
    private ConstructionBlueprint reservedBlueprint;
    private int reservedDeliveryAmount;
    private ResourceType reservedResourceType;
    private int reservedResourceAmount;
    private IStorage reservedStorage;
    private ResourceType reservedStorageType;
    private int reservedStorageAmount;
    private Vector2Int reservedStoragePosition;
    private Task activeTask;
    private Task reservedAdditionalHaulTask;
    private ResourceItem activeGroundItem;
    private ResourceItem reservedGroundItem;
    private bool groundItemCollected;
    private bool failureRecordedForActiveTask;
    private bool applicationIsQuitting;
    private bool isFinalizingTask;
    private IFoodSource reservedFoodSource;

    public TaskFailureReason LastFailureReason { get; private set; }
    public string LastFailureDetails { get; private set; } = "Nenhuma";
    public float LastFailureTime { get; private set; } = -1f;
    public Vector2Int? DiagnosticDestination { get; private set; }
    public int DiagnosticPathLength { get; private set; }
    public int DiagnosticPathIndex { get; private set; }
    public int DiagnosticReplanCount { get; private set; }
    public int DiagnosticMaxReplans { get; private set; }
    public int LastMealConsumedPortions { get; private set; }
    public string LastMealDetails { get; private set; } = "Nenhuma refeição executada.";

    public Task ActiveTask => activeTask;
    public bool HasStorageReservation =>
        reservedStorage != null && reservedStorageAmount > 0;
    public Vector2Int ReservedStoragePosition => reservedStoragePosition;
    public ResourceType ReservedStorageType => reservedStorageType;
    public int ReservedStorageAmount => reservedStorageAmount;
    public bool HasResourceReservation => reservedResourceAmount > 0;
    public ResourceType ReservedResourceType => reservedResourceType;
    public int ReservedResourceAmount => reservedResourceAmount;
    public bool HasBlueprintReservation =>
        reservedBlueprint != null && reservedDeliveryAmount > 0;
    public int ReservedDeliveryAmount => reservedDeliveryAmount;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
        movement = GetComponent<DuplicantMovement>();
        inventory = GetComponent<DuplicantInventory>();
    }

    private void OnDisable()
    {
        if (!SaveGameRuntime.IsLoading
            && !applicationIsQuitting
            && !isFinalizingTask
            && (activeTask != null || controller.currentTask != null))
        {
            CancelActiveTaskState();
        }
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
    }

    public IEnumerator ExecuteTaskRoutine(Task task, List<Vector2Int> path)
    {
        ReleaseDeliveryReservation();
        ReleaseResourceReservation();
        ReleaseStorageReservation();
        ReleaseAdditionalHaulTask();
        activeTask = task;
        activeGroundItem = null;
        groundItemCollected = false;
        failureRecordedForActiveTask = false;
        ResetPathDiagnostics();

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
        ConstructionBlueprint bp = task.targetBlueprint;
        if (bp == null || bp.CurrentState != BlueprintState.WaitingMaterials)
        {
            CancelTask(task, TaskFailureReason.TargetInvalid,
                "O blueprint não está aguardando materiais.");
            yield break;
        }

        ResourceType reqResource = bp.requiredResource;
        int remainingNeeded = bp.GetRemainingNeededAmount();

        if (remainingNeeded <= 0)
        {
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "O blueprint não precisa de mais materiais.");
            yield break;
        }

        if (inventory.HasItem && inventory.CarriedType != reqResource)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        int availableAmount = StockpileManager.Instance != null
            ? StockpileManager.Instance.GetAvailableAmount(reqResource)
            : 0;

        int amountToFetch = Mathf.Min(
            inventory.maxCapacity,
            remainingNeeded,
            availableAmount
        );
        amountToFetch = bp.ReserveDelivery(amountToFetch);

        if (amountToFetch <= 0)
        {
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "Não há material disponível para reservar.");
            yield break;
        }

        if (StockpileManager.Instance == null
            || !StockpileManager.Instance.ReserveResource(
                reqResource,
                amountToFetch
            ))
        {
            bp.CancelDeliveryReservation(amountToFetch);
            CancelTask(task, TaskFailureReason.ReservationFailed,
                "A reserva global do material falhou.");
            yield break;
        }

        reservedBlueprint = bp;
        reservedDeliveryAmount = amountToFetch;
        reservedResourceType = reqResource;
        reservedResourceAmount = amountToFetch;

        yield return StartCoroutine(FetchResourceRoutine(reqResource, amountToFetch));

        if (bp == null)
        {
            AbortDeliveryTask(
                task,
                TaskFailureReason.TargetDestroyed,
                "O blueprint foi destruído antes da entrega."
            );
            yield break;
        }

        if (!inventory.HasItem)
        {
            ReleaseDeliveryReservation();
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "Nenhum material pôde ser coletado.");
            yield break;
        }

        int undeliverableReservedAmount =
            reservedDeliveryAmount - inventory.CarriedAmount;

        if (undeliverableReservedAmount > 0)
        {
            bp.CancelDeliveryReservation(undeliverableReservedAmount);
            reservedDeliveryAmount -= undeliverableReservedAmount;
            StockpileManager.Instance?.UnreserveResource(
                reservedResourceType,
                undeliverableReservedAmount
            );
            reservedResourceAmount -= undeliverableReservedAmount;
        }

        List<Vector2Int> pathToBP = TaskNavigationUtility.GetPathToTask(controller.gridPosition, task, controller.capabilityProfile);
        yield return StartCoroutine(FollowTaskPath(task, pathToBP));

        if (!pathFollowSucceeded)
        {
            ReleaseDeliveryReservation();
            CancelTask(task, TaskFailureReason.PathBlocked,
                "Não foi possível chegar ao blueprint.");
            yield break;
        }

        if (bp == null)
        {
            AbortDeliveryTask(
                task,
                TaskFailureReason.TargetDestroyed,
                "O blueprint foi destruído durante a entrega."
            );
            yield break;
        }

        if (bp != null && inventory.CarriedType.HasValue)
        {
            int deliveredAmount = bp.DeliverResource(
                inventory.CarriedType.Value,
                inventory.CarriedAmount
            );

            if (deliveredAmount <= 0)
            {
                AbortDeliveryTask(
                    task,
                    TaskFailureReason.DeliveryRejected,
                    "O blueprint recusou a entrega."
                );
                yield break;
            }

            reservedResourceAmount = Mathf.Max(
                0,
                reservedResourceAmount - deliveredAmount
            );

            inventory.RemoveItem(
                inventory.CarriedType.Value,
                deliveredAmount
            );

            ClearDeliveryReservationTracking();
            ClearResourceReservationTracking();
        }

        TaskManager.Instance?.RemoveTask(task);
        bp?.NotifyTaskEnded(task);
        FinishCurrentTask();
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

        // 1. Caminha e recalcula se o mundo mudar durante o percurso.
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

        // 2. Se o colono ainda assim estiver PISANDO no tile do bloco, força o passo de afastamento
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

        // 3. Execução da Construção a uma distância segura
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

    private IEnumerator FetchResourceRoutine(ResourceType type, int targetAmount)
    {
        if (targetAmount <= 0)
        {
            yield break;
        }

        if (inventory.HasItem && inventory.CarriedType != type)
        {
            yield break;
        }

        int remainingAmount = Mathf.Max(
            0,
            targetAmount - inventory.CarriedAmount
        );

        if (remainingAmount <= 0)
        {
            yield break;
        }

        if (StructureManager.Instance != null)
        {
            List<StorageStructure> storages =
                StructureManager.Instance.GetStoragesWithResource(type);

            foreach (StorageStructure storage in storages)
            {
                if (remainingAmount <= 0 || inventory.SpaceRemaining <= 0)
                {
                    break;
                }

                List<Vector2Int> storagePath =
                    TaskNavigationUtility.GetPathToInteractionPosition(
                        controller.gridPosition,
                        storage.GridPosition
                    );

                if (storagePath == null)
                {
                    continue;
                }

                yield return StartCoroutine(
                    FollowPathToInteractionPosition(
                        storage.GridPosition,
                        storagePath
                    )
                );

                if (!pathFollowSucceeded || storage == null)
                {
                    continue;
                }

                int requestedFromStorage = Mathf.Min(
                    remainingAmount,
                    inventory.SpaceRemaining,
                    storage.GetLocalAmount(type)
                );

                if (requestedFromStorage <= 0
                    || !storage.WithdrawItem(type, requestedFromStorage))
                {
                    continue;
                }

                int acceptedAmount = inventory.AddItem(
                    type,
                    requestedFromStorage
                );

                int rejectedAmount = requestedFromStorage - acceptedAmount;
                if (rejectedAmount > 0)
                {
                    storage.StoreItem(type, rejectedAmount);
                }

                remainingAmount -= acceptedAmount;
            }
        }

        if (remainingAmount <= 0 || inventory.SpaceRemaining <= 0)
        {
            yield break;
        }

        ResourceItem[] itemsOnGround = Object.FindObjectsByType<ResourceItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var item in itemsOnGround)
        {
            if (remainingAmount <= 0 || inventory.SpaceRemaining <= 0)
            {
                break;
            }

            if (item == null || !item.gameObject.activeInHierarchy || item.type != type || item.amount <= 0)
                continue;

            Vector2Int itemGridPos = GridManager.Instance.WorldToGridPosition(item.transform.position);
            List<Vector2Int> path = PathfindingAStar.Instance?.FindPath(controller.gridPosition, itemGridPos);

            if (path != null)
            {
                yield return StartCoroutine(FollowPathToPosition(itemGridPos, path));

                if (!pathFollowSucceeded)
                {
                    continue;
                }

                if (item == null || !item.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int requestedAmount = Mathf.Min(
                    remainingAmount,
                    inventory.SpaceRemaining
                );

                if (!item.TryTake(requestedAmount, out int takenAmount))
                {
                    continue;
                }

                int acceptedAmount = inventory.AddItem(type, takenAmount);
                int rejectedAmount = takenAmount - acceptedAmount;

                if (rejectedAmount > 0)
                {
                    item.ReturnAmount(rejectedAmount);
                }

                remainingAmount -= acceptedAmount;

                if (item.amount <= 0)
                {
                    item.Recycle();
                }
            }
        }
    }

    private IEnumerator ExecuteGroundHaulRoutine(
        Task task,
        List<Vector2Int> initialPath)
    {
        ResourceItem item = task.targetItem;
        activeGroundItem = item;

        if (item == null
            || !item.gameObject.activeInHierarchy
            || !item.IsReadyForHaul
            || item.amount <= 0
            || StructureManager.Instance == null)
        {
            RemoveInvalidTask(task);
            yield break;
        }

        if (!item.TryReserve(this))
        {
            CancelTask(task, TaskFailureReason.ResourceUnavailable,
                "Outro duplicant já está coletando este item.");
            yield break;
        }

        reservedGroundItem = item;

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

        reservedStorage = storage;
        reservedStorageType = item.type;
        reservedStorageAmount = amountToStore;
        reservedStoragePosition = storage.GridPosition;

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

        groundItemCollected = true;

        int unusedReservation =
            reservedStorageAmount - inventory.CarriedAmount;

        if (unusedReservation > 0)
        {
            StorageStructure storageWithReservation =
                reservedStorage as StorageStructure;

            if (storageWithReservation == null)
            {
                FailGroundHaulAfterPickup(
                    task,
                    TaskFailureReason.StorageUnavailable,
                    "O baú reservado deixou de existir."
                );
                yield break;
            }

            storageWithReservation.ReleaseReservedSpace(
                reservedStorageType,
                unusedReservation
            );

            reservedStorageAmount -= unusedReservation;
        }

        if (item.amount <= 0)
        {
            item.Recycle();
        }

        yield return StartCoroutine(CollectAdditionalGroundItems(item.type));

        List<Vector2Int> pathToStorage =
            TaskNavigationUtility.GetPathToInteractionPosition(
                controller.gridPosition,
                reservedStoragePosition,
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
                reservedStoragePosition,
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
        groundItemCollected = false;

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
            reservedAdditionalHaulTask = additionalTask;

            if (item == null
                || !item.gameObject.activeInHierarchy
                || !item.IsReadyForHaul
                || item.type != type
                || item.amount <= 0)
            {
                ReleaseAdditionalHaulTask(true);
                continue;
            }

            if (!item.TryReserve(this))
            {
                ReleaseAdditionalHaulTask();
                yield break;
            }

            reservedGroundItem = item;

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

    private IEnumerator FollowTaskPath(Task task, List<Vector2Int> initialPath)
    {
        pathFollowSucceeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;
        const int maxReplans = 8;
        BeginPathDiagnostics(task.gridPosition, currentPath, maxReplans);

        while (true)
        {
            UpdatePathDiagnostics(currentPath, pathIndex, replanCount);

            if (task == null || currentPath == null)
            {
                RecordFailure(
                    TaskFailureReason.TargetUnreachable,
                    "Não existe caminho até a tarefa."
                );
                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();
                currentPath = RecalculateTaskPath(task);
                pathIndex = 0;
                replanCount++;

                if (replanCount > maxReplans)
                {
                    RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                pathFollowSucceeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath = RecalculateTaskPath(task);
                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.PathBlocked
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até a tarefa foi bloqueado."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            controller.currentState = DuplicantController.WorkerState.Moving;

            MovementType moveType = movement.DeduceMovementType(
                controller.gridPosition,
                nextTile
            );

            yield return movement.MoveToTile(
                nextTile,
                moveType,
                movement.GetCurrentMoveSpeed(moveType)
            );

            if (!movement.LastMoveSucceeded)
            {
                currentPath = RecalculateTaskPath(task);
                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }

    private IEnumerator FollowPathToPosition(
        Vector2Int targetPosition,
        List<Vector2Int> initialPath)
    {
        pathFollowSucceeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;
        const int maxReplans = 8;
        BeginPathDiagnostics(targetPosition, currentPath, maxReplans);

        while (true)
        {
            UpdatePathDiagnostics(currentPath, pathIndex, replanCount);

            if (currentPath == null)
            {
                RecordFailure(
                    TaskFailureReason.TargetUnreachable,
                    "Não existe caminho até o alvo."
                );
                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();
                currentPath = PathfindingAStar.Instance?.FindPath(
                    controller.gridPosition,
                    targetPosition
                );
                pathIndex = 0;
                replanCount++;

                if (replanCount > maxReplans)
                {
                    RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                pathFollowSucceeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath = PathfindingAStar.Instance?.FindPath(
                    controller.gridPosition,
                    targetPosition
                );
                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.PathBlocked
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até o alvo foi bloqueado."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            controller.currentState = DuplicantController.WorkerState.Moving;

            MovementType moveType = movement.DeduceMovementType(
                controller.gridPosition,
                nextTile
            );

            yield return movement.MoveToTile(
                nextTile,
                moveType,
                movement.GetCurrentMoveSpeed(moveType)
            );

            if (!movement.LastMoveSucceeded)
            {
                currentPath = PathfindingAStar.Instance?.FindPath(
                    controller.gridPosition,
                    targetPosition
                );
                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }

    private IEnumerator FollowPathToInteractionPosition(
        Vector2Int targetPosition,
        List<Vector2Int> initialPath,
        Vector2Int? fixedInteractionPosition = null)
    {
        pathFollowSucceeded = false;

        List<Vector2Int> currentPath = initialPath;
        int pathIndex = 0;
        int replanCount = 0;
        const int maxReplans = 8;
        BeginPathDiagnostics(targetPosition, currentPath, maxReplans);

        while (true)
        {
            UpdatePathDiagnostics(currentPath, pathIndex, replanCount);

            if (currentPath == null)
            {
                RecordFailure(
                    TaskFailureReason.StorageUnreachable,
                    "Não existe caminho até a posição de interação."
                );
                yield break;
            }

            if (movement.ShouldFall())
            {
                yield return movement.HandleFallingRoutine();

                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (replanCount > maxReplans)
                {
                    RecordFailure(
                        TaskFailureReason.ReplanLimitReached,
                        "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            if (pathIndex >= currentPath.Count)
            {
                if (fixedInteractionPosition.HasValue
                    && controller.gridPosition != fixedInteractionPosition.Value)
                {
                    currentPath = RecalculateInteractionPath(
                        targetPosition,
                        fixedInteractionPosition);
                    pathIndex = 0;
                    replanCount++;

                    if (currentPath == null || replanCount > maxReplans)
                    {
                        RecordFailure(
                            TaskFailureReason.StorageUnreachable,
                            "A posição reservada de interação ficou inacessível.");
                        yield break;
                    }

                    continue;
                }

                pathFollowSucceeded = true;
                yield break;
            }

            Vector2Int nextTile = currentPath[pathIndex];

            if (!CanTraverseTo(nextTile))
            {
                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.StorageUnreachable
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O caminho até a interação foi bloqueado."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            controller.currentState = DuplicantController.WorkerState.Moving;

            MovementType moveType = movement.DeduceMovementType(
                controller.gridPosition,
                nextTile
            );

            yield return movement.MoveToTile(
                nextTile,
                moveType,
                movement.GetCurrentMoveSpeed(moveType)
            );

            if (!movement.LastMoveSucceeded)
            {
                currentPath = RecalculateInteractionPath(
                    targetPosition,
                    fixedInteractionPosition);

                pathIndex = 0;
                replanCount++;

                if (currentPath == null || replanCount > maxReplans)
                {
                    RecordFailure(
                        currentPath == null
                            ? TaskFailureReason.MovementFailed
                            : TaskFailureReason.ReplanLimitReached,
                        currentPath == null
                            ? "O movimento falhou e não existe rota alternativa."
                            : "Limite de recálculos do caminho atingido."
                    );
                    yield break;
                }

                continue;
            }

            pathIndex++;
        }
    }

    private List<Vector2Int> RecalculateInteractionPath(
        Vector2Int targetPosition,
        Vector2Int? fixedInteractionPosition)
    {
        if (fixedInteractionPosition.HasValue)
        {
            if (controller.gridPosition == fixedInteractionPosition.Value)
            {
                return new List<Vector2Int>();
            }

            return PathfindingAStar.Instance?.FindPath(
                controller.gridPosition,
                fixedInteractionPosition.Value,
                controller.capabilityProfile);
        }

        return TaskNavigationUtility.GetPathToInteractionPosition(
            controller.gridPosition,
            targetPosition,
            true,
            controller.capabilityProfile);
    }

    private List<Vector2Int> RecalculateTaskPath(Task task)
    {
        return TaskNavigationUtility.GetPathToTask(
            controller.gridPosition,
            task,
            controller.capabilityProfile
        );
    }

    private bool CanTraverseTo(Vector2Int nextTile)
    {
        return NavGraphGenerator.Instance != null
            && NavGraphGenerator.Instance.CanTraverse(
                controller.gridPosition,
                nextTile
            );
    }

    public void CancelActiveTaskState()
    {
        if (isFinalizingTask) return;
        isFinalizingTask = true;

        if (activeTask != null || controller.currentTask != null)
        {
            RecordFailure(
                TaskFailureReason.Interrupted,
                "A execução foi interrompida externamente."
            );
        }

        // Interrompe também as rotinas filhas de movimento e execução.
        StopAllCoroutines();

        ReleaseAllReservations();

        if (inventory != null && inventory.HasItem)
        {
            inventory.DropCarriedItem(GetSafeDropPosition());
        }

        if (groundItemCollected)
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

    private void ReleaseAllReservations()
    {
        ReleaseDeliveryReservation();
        ReleaseResourceReservation();
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
        if (reservedBlueprint != null && reservedDeliveryAmount > 0)
        {
            reservedBlueprint.CancelDeliveryReservation(
                reservedDeliveryAmount
            );
        }

        ClearDeliveryReservationTracking();
    }

    private void ClearDeliveryReservationTracking()
    {
        reservedBlueprint = null;
        reservedDeliveryAmount = 0;
    }

    private void ReleaseResourceReservation()
    {
        if (reservedResourceAmount > 0)
        {
            StockpileManager.Instance?.UnreserveResource(
                reservedResourceType,
                reservedResourceAmount
            );
        }

        ClearResourceReservationTracking();
    }

    private void ClearResourceReservationTracking()
    {
        reservedResourceAmount = 0;
    }

    private void ReleaseStorageReservation()
    {
        StorageStructure storage = reservedStorage as StorageStructure;

        if (storage != null && reservedStorageAmount > 0)
        {
            storage.ReleaseReservedSpace(
                reservedStorageType,
                reservedStorageAmount
            );
        }

        ClearStorageReservationTracking();
    }

    private void ReleaseGroundItemReservation()
    {
        if (reservedGroundItem != null)
        {
            reservedGroundItem.ReleaseReservation(this);
        }

        reservedGroundItem = null;
    }

    private void ReleaseAdditionalHaulTask(bool removeTask = false)
    {
        if (reservedAdditionalHaulTask == null)
        {
            return;
        }

        if (removeTask)
        {
            TaskManager.Instance?.RemoveTask(reservedAdditionalHaulTask);
        }
        else
        {
            TaskManager.Instance?.ReleaseTask(reservedAdditionalHaulTask);
        }

        reservedAdditionalHaulTask = null;
    }

    private void ClearStorageReservationTracking()
    {
        reservedStorage = null;
        reservedStorageAmount = 0;
        reservedStoragePosition = Vector2Int.zero;
    }

    private bool TryStoreReservedInventory()
    {
        StorageStructure storage = reservedStorage as StorageStructure;

        if (storage == null
            || !inventory.HasItem
            || inventory.CarriedType != reservedStorageType)
        {
            return false;
        }

        int amountToStore = inventory.CarriedAmount;
        int removedAmount = inventory.RemoveItem(
            reservedStorageType,
            amountToStore
        );

        if (removedAmount != amountToStore)
        {
            if (removedAmount > 0)
            {
                inventory.AddItem(reservedStorageType, removedAmount);
            }

            return false;
        }

        if (storage.StoreReservedItem(
                reservedStorageType,
                amountToStore))
        {
            return true;
        }

        // Se o baú recusou a entrega, devolve a carga ao inventário.
        inventory.AddItem(reservedStorageType, amountToStore);
        return false;
    }

    private bool TryExpandStorageReservation(int amount)
    {
        StorageStructure storage = reservedStorage as StorageStructure;

        if (storage == null || amount <= 0
            || !storage.TryReserveSpace(reservedStorageType, amount))
        {
            return false;
        }

        reservedStorageAmount += amount;
        return true;
    }

    private void ReleaseStorageReservationAmount(int amount)
    {
        StorageStructure storage = reservedStorage as StorageStructure;

        if (storage == null || amount <= 0)
        {
            return;
        }

        storage.ReleaseReservedSpace(reservedStorageType, amount);
        reservedStorageAmount = Mathf.Max(0, reservedStorageAmount - amount);
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
        groundItemCollected = false;
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
        if (activeGroundItem != null
            && activeGroundItem.gameObject.activeInHierarchy
            && activeGroundItem.amount > 0)
        {
            TaskManager.Instance?.AddHaulTask(activeGroundItem);
        }
    }

    private void ClearGroundHaulTracking()
    {
        activeTask = null;
        activeGroundItem = null;
        groundItemCollected = false;
    }

    private void CancelTask(
        Task task,
        TaskFailureReason reason = TaskFailureReason.Interrupted,
        string details = "A tarefa foi interrompida.")
    {
        if (!failureRecordedForActiveTask)
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
        LastFailureReason = TaskFailureReason.None;
        LastFailureDetails = "Nenhuma";
        LastFailureTime = -1f;
        failureRecordedForActiveTask = false;
    }

    private void RecordFailure(
        TaskFailureReason reason,
        string details)
    {
        LastFailureReason = reason;
        LastFailureDetails = string.IsNullOrWhiteSpace(details)
            ? reason.ToString()
            : details;
        LastFailureTime = Time.time;
        failureRecordedForActiveTask = true;
    }

    private void BeginPathDiagnostics(
        Vector2Int destination,
        List<Vector2Int> path,
        int maxReplans)
    {
        DiagnosticDestination = destination;
        DiagnosticPathLength = path != null ? path.Count : 0;
        DiagnosticPathIndex = 0;
        DiagnosticReplanCount = 0;
        DiagnosticMaxReplans = maxReplans;
    }

    private void UpdatePathDiagnostics(
        List<Vector2Int> path,
        int pathIndex,
        int replanCount)
    {
        DiagnosticPathLength = path != null ? path.Count : 0;
        DiagnosticPathIndex = pathIndex;
        DiagnosticReplanCount = replanCount;
    }

    private void ResetPathDiagnostics()
    {
        DiagnosticDestination = null;
        DiagnosticPathLength = 0;
        DiagnosticPathIndex = 0;
        DiagnosticReplanCount = 0;
        DiagnosticMaxReplans = 0;
    }

    private void FinishCurrentTask()
    {
        // Última barreira contra reservas órfãs em qualquer caminho de saída.
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
