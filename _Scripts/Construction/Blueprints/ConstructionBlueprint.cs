using System.Collections.Generic;
using UnityEngine;

public enum BlueprintState
{
    WaitingMaterials,
    ReadyToBuild,
    Completed
}

public class ConstructionBlueprint : MonoBehaviour
{
    [Header("Configurações do Blueprint")]
    public Vector2Int gridPosition;
    public string buildDefinitionId;
    public TileType targetTileType;
    public GridLayer buildLayer = GridLayer.Terrain;
    public ResourceType requiredResource;
    public int requiredAmount;

    [Header("Progresso do Suprimento")]
    public int deliveredAmount = 0;
    public int reservedDeliveryAmount = 0;

    [Header("Progresso de Montagem")]
    public float totalWorkRequired = 5f;
    public float currentWorkDone = 0f;

    public BlueprintState CurrentState { get; private set; } = BlueprintState.WaitingMaterials;

    private Task currentTask;
    private bool applicationIsQuitting;
    private bool footprintReserved;

    public StructureFootprintDefinition Footprint { get; private set; }

    private void Start()
    {
        // Garante a criação da tarefa caso o blueprint tenha sido
        // inicializado antes do TaskManager durante o carregamento da cena.
        CheckTaskState();
    }

    public void Initialize(
        Vector2Int pos,
        TileType tileType,
        ResourceType resource,
        int amount,
        float workRequired,
        GridLayer layer = GridLayer.Terrain,
        string definitionId = "",
        StructureFootprintDefinition footprint = null)
    {
        gridPosition = pos;
        buildDefinitionId = definitionId?.Trim() ?? string.Empty;
        targetTileType = tileType;
        buildLayer = layer;
        requiredResource = resource;
        requiredAmount = amount;
        totalWorkRequired = workRequired;
        Footprint = footprint ?? StructureFootprintSettings.Resolve(tileType);

        deliveredAmount = 0;
        reservedDeliveryAmount = 0;
        currentWorkDone = 0f;

        CheckTaskState();
    }

    public bool TryReserveFootprint()
    {
        if (footprintReserved)
        {
            return true;
        }

        if (GridManager.Instance == null)
        {
            return false;
        }

        Footprint = Footprint
            ?? StructureFootprintSettings.Resolve(targetTileType);
        footprintReserved = GridManager.Instance.RegisterFootprint(
            this,
            gridPosition,
            Footprint,
            true,
            buildLayer);
        return footprintReserved;
    }

    public void RestoreProgress(
        int restoredDeliveredAmount,
        float restoredWorkDone)
    {
        deliveredAmount = Mathf.Clamp(
            restoredDeliveredAmount,
            0,
            requiredAmount
        );
        reservedDeliveryAmount = 0;
        currentWorkDone = Mathf.Clamp(
            restoredWorkDone,
            0f,
            totalWorkRequired
        );
        currentTask = null;

        CurrentState = deliveredAmount < requiredAmount
            ? BlueprintState.WaitingMaterials
            : BlueprintState.ReadyToBuild;
    }

    public int GetRemainingNeededAmount()
    {
        return Mathf.Max(0, requiredAmount - (deliveredAmount + reservedDeliveryAmount));
    }

    public int ReserveDelivery(int requestedAmount)
    {
        int availableReservation = Mathf.Max(
            0,
            requiredAmount - deliveredAmount - reservedDeliveryAmount
        );

        int reservedAmount = Mathf.Clamp(
            requestedAmount,
            0,
            availableReservation
        );

        reservedDeliveryAmount += reservedAmount;
        return reservedAmount;
    }

    public void CancelDeliveryReservation(int amount)
    {
        reservedDeliveryAmount = Mathf.Max(0, reservedDeliveryAmount - amount);
    }

    public int DeliverResource(ResourceType type, int amount)
    {
        if (type != requiredResource || amount <= 0) return 0;

        int acceptedAmount = Mathf.Min(
            amount,
            Mathf.Max(0, requiredAmount - deliveredAmount)
        );

        if (acceptedAmount <= 0)
        {
            return 0;
        }

        if (StockpileManager.Instance == null
            || !StockpileManager.Instance.CommitReservedResource(
                type,
                acceptedAmount
            ))
        {
            return 0;
        }

        reservedDeliveryAmount = Mathf.Max(
            0,
            reservedDeliveryAmount - acceptedAmount
        );

        deliveredAmount += acceptedAmount;

        GameEvents.TriggerFloatingTextRequested($"+{acceptedAmount} {type}", transform.position, Color.green);

        CheckTaskState();
        return acceptedAmount;
    }

    public void NotifyTaskEnded(Task finishedTask)
    {
        if (currentTask != finishedTask)
        {
            return;
        }

        currentTask = null;
        CheckTaskState();
    }

    public bool ApplyWork(float workDelta)
    {
        if (CurrentState != BlueprintState.ReadyToBuild) return false;

        currentWorkDone += workDelta;

        if (currentWorkDone >= totalWorkRequired)
        {
            CompleteConstruction();
            return true;
        }

        return false;
    }

    public void CheckTaskState()
    {
        if (SaveGameRuntime.IsLoading)
        {
            return;
        }

        if (CurrentState == BlueprintState.Completed) return;

        if (deliveredAmount < requiredAmount)
        {
            CurrentState = BlueprintState.WaitingMaterials;

            bool hasQueuedSupplyTask = currentTask != null
                && currentTask.type == TaskType.HaulResource
                && TaskManager.Instance != null
                && TaskManager.Instance.ContainsTask(currentTask);

            if (GetRemainingNeededAmount() > 0
                && !hasQueuedSupplyTask
                && TaskManager.Instance != null)
            {
                if (currentTask != null)
                {
                    TaskManager.Instance.RemoveTask(currentTask);
                }

                int priority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(TaskType.HaulResource) : 5;
                currentTask = new Task(gridPosition, TaskType.HaulResource, targetTileType, null, priority)
                {
                    targetBlueprint = this
                };
                TaskManager.Instance.AddTask(currentTask);
            }
        }
        else
        {
            CurrentState = BlueprintState.ReadyToBuild;

            if (currentTask != null
                && currentTask.type == TaskType.BuildTile
                && TaskManager.Instance != null
                && TaskManager.Instance.ContainsTask(currentTask))
            {
                return;
            }

            if (TaskManager.Instance == null)
            {
                return;
            }

            if (currentTask != null)
            {
                TaskManager.Instance.RemoveTask(currentTask);
            }

            int priority = PriorityManager.Instance != null ? PriorityManager.Instance.GetCategoryPriority(TaskType.BuildTile) : 5;
            currentTask = new Task(gridPosition, TaskType.BuildTile, targetTileType, null, priority)
            {
                targetBlueprint = this
            };
            TaskManager.Instance.AddTask(currentTask);
        }
    }

    private void OnDestroy()
    {
        BlueprintManager.Instance?.UnregisterBlueprint(this);
        if (Application.isPlaying && !applicationIsQuitting
            && !SaveGameRuntime.IsLoading)
        {
            TaskManager.Instance?.CancelTasksForBlueprint(this);
        }
        GridManager.Instance?.UnregisterFootprint(this);
        footprintReserved = false;

        bool shouldCleanRuntimeState =
            Application.isPlaying
            && !applicationIsQuitting
            && !SaveGameRuntime.IsLoading;

        if (shouldCleanRuntimeState
            && CurrentState != BlueprintState.Completed
            && deliveredAmount > 0)
        {
            bool refunded = ItemSpawner.Instance != null
                && ItemSpawner.Instance.TrySpawnResource(
                    requiredResource,
                    transform.position,
                    deliveredAmount
                );

            if (!refunded)
            {
                Debug.LogError(
                    $"[ConstructionBlueprint] Falha ao devolver {deliveredAmount}x {requiredResource}.",
                    this
                );
            }
            else
            {
                deliveredAmount = 0;
            }

            StockpileManager.Instance?.RequestRefresh();
        }

        if (shouldCleanRuntimeState
            && CurrentState != BlueprintState.Completed
            && currentTask != null)
        {
            TaskManager.Instance?.RemoveTask(currentTask);
        }

        currentTask = null;
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
    }

    private void CompleteConstruction()
    {
        CurrentState = BlueprintState.Completed;

        if (currentTask != null)
        {
            TaskManager.Instance?.RemoveTask(currentTask);
        }

        GridManager.Instance?.UnregisterFootprint(this);
        footprintReserved = false;

        // GridManager é o único responsável por criar estruturas associadas
        // ao tile, evitando instanciar um baú duas vezes.
        GridManager.Instance?.SetBuildContent(
            gridPosition.x,
            gridPosition.y,
            buildDefinitionId,
            targetTileType,
            buildLayer);

        Destroy(gameObject);
    }
}
