using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProductionMachineBehaviour : MonoBehaviour,
    IInteractable,
    IConfiguredStructureBehaviour,
    IResourceDeliveryTarget
{
    [Header("Receitas")]
    [SerializeField] private List<ProductionRecipeSO> availableRecipes =
        new List<ProductionRecipeSO>();

    [Header("Pedido")]
    [Tooltip("Tempo sem alterações antes de publicar tarefas de entrega.")]
    [SerializeField, Min(0f)] private float orderConfirmationDelay = 0.5f;
    [SerializeField, Min(1)] private int maximumBatchesPerOrder = 100;

    [Header("Saída")]
    [SerializeField] private Vector3 outputWorldOffset = new Vector3(0.5f, 0.5f, 0f);

    private readonly Dictionary<ResourceType, int> inputBuffer =
        new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> incomingReservations =
        new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> completedOutputBuffer =
        new Dictionary<ResourceType, int>();

    private ConfiguredStructure configuredStructure;
    private Coroutine confirmationRoutine;
    private ProductionRecipeSO pendingRecipe;
    private int pendingBatches;
    private ProductionRecipeSO activeRecipe;
    private int remainingBatches;
    private float completedWork;
    private bool shuttingDown;

    public event Action StateChanged;

    public bool UsesSpecializedSaveData => false;
    public Vector2Int GridPosition => configuredStructure != null
        ? configuredStructure.GridPosition
        : GridManager.Instance != null
            ? GridManager.Instance.WorldToGridPosition(transform.position)
            : Vector2Int.zero;
    public IReadOnlyList<ProductionRecipeSO> AvailableRecipes => availableRecipes;
    public ProductionRecipeSO ActiveRecipe => activeRecipe;
    public int RemainingBatches => remainingBatches;
    public float CompletedWork => completedWork;
    public float WorkRequired => activeRecipe != null
        ? activeRecipe.WorkRequiredPerBatch
        : 0f;
    public bool HasActiveOrder => activeRecipe != null && remainingBatches > 0;
    public bool HasPendingOrder => pendingRecipe != null;
    public bool CanOperate => HasActiveOrder && HasInputsForCurrentBatch();
    public bool IsDeliveryTargetValid => !shuttingDown && HasActiveOrder;
    public Dictionary<ResourceType, int> GetInputBufferSnapshot() =>
        new Dictionary<ResourceType, int>(inputBuffer);
    public Dictionary<ResourceType, int> GetOutputBufferSnapshot() =>
        new Dictionary<ResourceType, int>(completedOutputBuffer);

    public void InitializeStructureBehaviour(ConfiguredStructure structure)
    {
        configuredStructure = structure;
        shuttingDown = false;
    }

    public void ReevaluateTasks()
    {
        EnsureTasks();
    }

    public void ShutdownStructureBehaviour()
    {
        shuttingDown = true;
        if (confirmationRoutine != null) StopCoroutine(confirmationRoutine);
        confirmationRoutine = null;
        CancelOrder();
        FlushCompletedOutputs();
    }

    public void OnInteract()
    {
        ProductionMachineUI.Instance?.Open(this);
    }

    public void RequestOrder(ProductionRecipeSO recipe, int batches)
    {
        if (recipe == null || HasActiveOrder || shuttingDown) return;
        pendingRecipe = recipe;
        pendingBatches = Mathf.Clamp(batches, 1, maximumBatchesPerOrder);

        if (confirmationRoutine != null) StopCoroutine(confirmationRoutine);
        confirmationRoutine = StartCoroutine(ConfirmOrderAfterDelay());
        StateChanged?.Invoke();
    }

    public void CancelPendingOrder()
    {
        if (confirmationRoutine != null) StopCoroutine(confirmationRoutine);
        confirmationRoutine = null;
        pendingRecipe = null;
        pendingBatches = 0;
        StateChanged?.Invoke();
    }

    public void CancelOrder()
    {
        CancelPendingOrder();
        activeRecipe = null;
        remainingBatches = 0;
        completedWork = 0f;
        incomingReservations.Clear();
        DropDictionary(inputBuffer);
        TaskManager.Instance?.CancelTasksForMachine(this);
        StateChanged?.Invoke();
    }

    public bool NeedsInput(ResourceType type)
    {
        return GetUnreservedNeed(type) > 0;
    }

    public bool HasOutstandingInput(ResourceType type)
    {
        if (!HasActiveOrder) return false;
        return activeRecipe.GetInputAmount(type)
            - GetAmount(inputBuffer, type) > 0;
    }

    public bool TryReserveInput(ResourceType type, int capacity, out int reservedAmount)
    {
        reservedAmount = Mathf.Min(Mathf.Max(0, capacity), GetUnreservedNeed(type));
        if (reservedAmount <= 0) return false;
        incomingReservations[type] = GetAmount(incomingReservations, type) + reservedAmount;
        StateChanged?.Invoke();
        return true;
    }

    public void ReleaseInputReservation(ResourceType type, int amount)
    {
        if (amount <= 0) return;
        incomingReservations[type] = Mathf.Max(
            0,
            GetAmount(incomingReservations, type) - amount);
        EnsureTasks();
        StateChanged?.Invoke();
    }

    public int AcceptReservedInput(ResourceType type, int amount)
    {
        int reserved = GetAmount(incomingReservations, type);
        int accepted = Mathf.Min(Mathf.Max(0, amount), reserved);
        if (accepted <= 0) return 0;

        incomingReservations[type] = reserved - accepted;
        inputBuffer[type] = GetAmount(inputBuffer, type) + accepted;
        EnsureTasks();
        StateChanged?.Invoke();
        return accepted;
    }

    public bool ApplyOperationWork(float workAmount)
    {
        if (!CanOperate || workAmount <= 0f) return false;
        completedWork = Mathf.Min(WorkRequired, completedWork + workAmount);
        StateChanged?.Invoke();

        if (completedWork < WorkRequired) return false;
        CompleteBatch();
        return true;
    }

    public void RestoreState(
        string recipeId,
        int batches,
        float restoredWork,
        IEnumerable<ResourceAmountSaveData> restoredInputs,
        IEnumerable<ResourceAmountSaveData> restoredOutputs)
    {
        inputBuffer.Clear();
        incomingReservations.Clear();
        completedOutputBuffer.Clear();

        activeRecipe = FindRecipe(recipeId);
        remainingBatches = activeRecipe != null ? Mathf.Max(0, batches) : 0;
        completedWork = activeRecipe != null
            ? Mathf.Clamp(restoredWork, 0f, activeRecipe.WorkRequiredPerBatch)
            : 0f;

        RestoreDictionary(inputBuffer, restoredInputs);
        RestoreDictionary(completedOutputBuffer, restoredOutputs);
        FlushCompletedOutputs();
        EnsureTasks();
        StateChanged?.Invoke();
    }

    private IEnumerator ConfirmOrderAfterDelay()
    {
        if (orderConfirmationDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(orderConfirmationDelay);
        }

        confirmationRoutine = null;
        activeRecipe = pendingRecipe;
        remainingBatches = pendingBatches;
        pendingRecipe = null;
        pendingBatches = 0;
        completedWork = 0f;
        EnsureTasks();
        StateChanged?.Invoke();
    }

    private void EnsureTasks()
    {
        if (!HasActiveOrder || TaskManager.Instance == null) return;

        if (HasInputsForCurrentBatch())
        {
            TaskManager.Instance.AddMachineOperationTask(this);
            return;
        }

        IReadOnlyList<ProductionRecipeSO.Ingredient> inputs = activeRecipe.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            ProductionRecipeSO.Ingredient input = inputs[i];
            if (input != null && NeedsInput(input.resourceType))
            {
                TaskManager.Instance.AddMachineSupplyTask(this, input.resourceType);
            }
        }
    }

    private bool HasInputsForCurrentBatch()
    {
        if (!HasActiveOrder) return false;
        IReadOnlyList<ProductionRecipeSO.Ingredient> inputs = activeRecipe.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            ProductionRecipeSO.Ingredient input = inputs[i];
            if (input != null
                && GetAmount(inputBuffer, input.resourceType)
                    < activeRecipe.GetInputAmount(input.resourceType))
            {
                return false;
            }
        }
        return true;
    }

    private int GetUnreservedNeed(ResourceType type)
    {
        if (!HasActiveOrder) return 0;
        return Mathf.Max(
            0,
            activeRecipe.GetInputAmount(type)
                - GetAmount(inputBuffer, type)
                - GetAmount(incomingReservations, type));
    }

    private void CompleteBatch()
    {
        IReadOnlyList<ProductionRecipeSO.Ingredient> inputs = activeRecipe.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            ProductionRecipeSO.Ingredient input = inputs[i];
            if (input != null)
            {
                inputBuffer[input.resourceType] = Mathf.Max(
                    0,
                    GetAmount(inputBuffer, input.resourceType) - input.amount);
            }
        }

        IReadOnlyList<ProductionRecipeSO.Ingredient> outputs = activeRecipe.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            ProductionRecipeSO.Ingredient output = outputs[i];
            if (output != null)
            {
                completedOutputBuffer[output.resourceType] =
                    GetAmount(completedOutputBuffer, output.resourceType)
                    + output.amount;
            }
        }

        completedWork = 0f;
        remainingBatches = Mathf.Max(0, remainingBatches - 1);
        if (remainingBatches == 0) activeRecipe = null;

        FlushCompletedOutputs();
        EnsureTasks();
        StateChanged?.Invoke();
    }

    private void FlushCompletedOutputs()
    {
        if (ItemSpawner.Instance == null) return;
        List<ResourceType> types = new List<ResourceType>(completedOutputBuffer.Keys);
        for (int i = 0; i < types.Count; i++)
        {
            ResourceType type = types[i];
            int amount = GetAmount(completedOutputBuffer, type);
            if (amount > 0 && ItemSpawner.Instance.TrySpawnResource(
                type,
                transform.position + outputWorldOffset,
                amount))
            {
                completedOutputBuffer[type] = 0;
            }
        }
    }

    private void DropDictionary(Dictionary<ResourceType, int> resources)
    {
        if (ItemSpawner.Instance == null) return;
        List<ResourceType> types = new List<ResourceType>(resources.Keys);
        for (int i = 0; i < types.Count; i++)
        {
            ResourceType type = types[i];
            int amount = GetAmount(resources, type);
            if (amount > 0 && ItemSpawner.Instance.TrySpawnResource(
                type,
                transform.position + outputWorldOffset,
                amount))
            {
                resources[type] = 0;
            }
        }
    }

    private static int GetAmount(Dictionary<ResourceType, int> source, ResourceType type)
    {
        return source.TryGetValue(type, out int amount) ? amount : 0;
    }

    private ProductionRecipeSO FindRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return null;
        for (int i = 0; i < availableRecipes.Count; i++)
        {
            ProductionRecipeSO recipe = availableRecipes[i];
            if (recipe != null && recipe.RecipeId == recipeId) return recipe;
        }
        return null;
    }

    private static void RestoreDictionary(
        Dictionary<ResourceType, int> destination,
        IEnumerable<ResourceAmountSaveData> source)
    {
        if (source == null) return;
        foreach (ResourceAmountSaveData entry in source)
        {
            if (entry != null && entry.amount > 0)
            {
                destination[(ResourceType)entry.resourceType] = entry.amount;
            }
        }
    }
}
