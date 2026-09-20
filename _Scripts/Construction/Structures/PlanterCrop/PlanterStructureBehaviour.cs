using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

public enum PlanterState
{
    Empty = 0,
    Growing = 1,
    Ready = 2,
    WaitingMaterials = 3
}

[DisallowMultipleComponent]
[RequireComponent(typeof(ConfiguredStructure))]
public sealed class PlanterStructureBehaviour : MonoBehaviour,
    IConfiguredStructureBehaviour,
    IInteractable,
    IResourceDeliveryTarget
{
    [SerializeField] private PlanterSettingsSO settings;

    private ConfiguredStructure structure;
    private PlanterCrop crop;
    private SpriteRenderer cropRenderer;
    private SortingGroup sortingGroup;
    private PlanterState state = PlanterState.Empty;
    private float remainingGrowthTime;
    private float growthCompletesAt;
    private bool initialized;
    private FloraDefinitionSO selectedCrop;
    private readonly Dictionary<ResourceType, int> deliveredMaterials =
        new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> incomingReservations =
        new Dictionary<ResourceType, int>();

    public event Action StateChanged;

    public bool UsesSpecializedSaveData => false;
    public PlanterState State => state;
    public bool IsCropReady => initialized && state == PlanterState.Ready;
    public float RemainingGrowthTime => state == PlanterState.Growing
        ? Mathf.Max(0f, growthCompletesAt - Time.time)
        : 0f;
    public Vector2Int GridPosition => structure != null
        ? structure.GridPosition
        : GridManager.Instance != null
            ? GridManager.Instance.WorldToGridPosition(transform.position)
            : Vector2Int.zero;
    public string SelectedCropId => selectedCrop != null
        ? selectedCrop.floraId
        : string.Empty;
    public PlanterSettingsSO Settings => settings;
    public Dictionary<ResourceType, int> GetDeliveredMaterialsSnapshot() =>
        new Dictionary<ResourceType, int>(deliveredMaterials);
    public bool IsDeliveryTargetValid => initialized
        && state == PlanterState.WaitingMaterials
        && selectedCrop != null;

    public void InitializeStructureBehaviour(ConfiguredStructure owner)
    {
        structure = owner;
        initialized = true;
        ConfigureRendering();
        EnsureCropObject();

        if (settings != null && settings.StartPlanted)
        {
            selectedCrop = settings.GetDefaultCrop();
            BeginGrowth();
        }
        else
        {
            SetState(PlanterState.Empty, 0f);
        }
    }

    public void ShutdownStructureBehaviour()
    {
        TaskManager.Instance?.CancelTasksForDeliveryTarget(this);
        DropDeliveredMaterials();
        incomingReservations.Clear();
        initialized = false;
        CancelInvoke(nameof(CompleteGrowth));
        structure = null;
        if (crop != null) Destroy(crop.gameObject);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(CompleteGrowth));
    }

    private void OnEnable()
    {
        if (!initialized || state != PlanterState.Growing)
        {
            return;
        }

        float delay = Mathf.Max(0f, growthCompletesAt - Time.time);
        if (delay <= 0f)
        {
            CompleteGrowth();
        }
        else
        {
            Invoke(nameof(CompleteGrowth), delay);
        }
    }

    public void BeginGrowth()
    {
        if (settings == null || selectedCrop == null)
        {
            SetState(PlanterState.Empty, 0f);
            Debug.LogWarning(
                "[Planter] Settings ou Crop Definition não configurado.",
                this);
            return;
        }

        deliveredMaterials.Clear();
        incomingReservations.Clear();
        SetState(
            PlanterState.Growing,
            settings.GetGrowthDuration(selectedCrop));
    }

    public bool TrySelectCrop(FloraDefinitionSO cropDefinition)
    {
        if (!initialized
            || state != PlanterState.Empty
            || settings == null
            || !settings.AllowsCrop(cropDefinition))
        {
            return false;
        }

        selectedCrop = cropDefinition;
        deliveredMaterials.Clear();
        incomingReservations.Clear();
        EnsureCropObject();
        crop?.Bind(this, selectedCrop);
        if (HasAllPlantingMaterials())
        {
            BeginGrowth();
        }
        else
        {
            SetState(PlanterState.WaitingMaterials, 0f);
            ReevaluateTasks();
        }
        return true;
    }

    public bool IsCropAvailable(FloraDefinitionSO cropDefinition)
    {
        if (cropDefinition == null || !settings.AllowsCrop(cropDefinition))
        {
            return false;
        }

        if (cropDefinition.plantingRequirements == null
            || cropDefinition.plantingRequirements.Count == 0)
        {
            return true;
        }

        StockpileManager stockpile = StockpileManager.Instance;
        if (stockpile == null) return false;
        foreach (PlantingRequirement requirement in cropDefinition.plantingRequirements)
        {
            if (requirement != null
                && stockpile.GetAvailableAmount(requirement.resourceType)
                    < requirement.amount)
            {
                return false;
            }
        }

        return true;
    }

    public bool NeedsInput(ResourceType type) => GetUnreservedNeed(type) > 0;

    public bool HasOutstandingInput(ResourceType type)
    {
        return IsDeliveryTargetValid && GetRequiredAmount(type)
            - GetAmount(deliveredMaterials, type) > 0;
    }

    public bool TryReserveInput(
        ResourceType type,
        int capacity,
        out int reservedAmount)
    {
        reservedAmount = Mathf.Min(Mathf.Max(0, capacity), GetUnreservedNeed(type));
        if (reservedAmount <= 0) return false;
        incomingReservations[type] = GetAmount(incomingReservations, type)
            + reservedAmount;
        StateChanged?.Invoke();
        return true;
    }

    public void ReleaseInputReservation(ResourceType type, int amount)
    {
        if (amount <= 0) return;
        incomingReservations[type] = Mathf.Max(
            0,
            GetAmount(incomingReservations, type) - amount);
        ReevaluateTasks();
        StateChanged?.Invoke();
    }

    public int AcceptReservedInput(ResourceType type, int amount)
    {
        int reserved = GetAmount(incomingReservations, type);
        int accepted = Mathf.Min(Mathf.Max(0, amount), reserved);
        if (accepted <= 0) return 0;

        incomingReservations[type] = reserved - accepted;
        deliveredMaterials[type] = GetAmount(deliveredMaterials, type) + accepted;
        if (HasAllPlantingMaterials()) BeginGrowth();
        else ReevaluateTasks();
        StateChanged?.Invoke();
        return accepted;
    }

    public void ReevaluateTasks()
    {
        if (!IsDeliveryTargetValid || TaskManager.Instance == null) return;
        foreach (PlantingRequirement requirement in selectedCrop.plantingRequirements)
        {
            if (requirement != null && NeedsInput(requirement.resourceType))
            {
                TaskManager.Instance.AddResourceDeliveryTask(
                    this,
                    requirement.resourceType);
            }
        }
    }

    public void OnInteract()
    {
        PlanterInspectUI.Instance?.Open(this);
    }

    public void HandleCropHarvested()
    {
        if (!initialized || state != PlanterState.Ready)
        {
            return;
        }

        if (settings != null && settings.AutoReplant)
        {
            FloraDefinitionSO harvestedCrop = selectedCrop;
            SetState(PlanterState.Empty, 0f);
            if (IsCropAvailable(harvestedCrop))
            {
                TrySelectCrop(harvestedCrop);
            }
        }
        else
        {
            SetState(PlanterState.Empty, 0f);
        }
    }

    public void RestoreState(
        PlanterState savedState,
        float savedRemainingTime,
        string savedCropId,
        IEnumerable<ResourceAmountSaveData> restoredMaterials = null)
    {
        if (!initialized)
        {
            return;
        }

        selectedCrop = FloraManager.Instance?.GetDefinitionById(savedCropId);
        deliveredMaterials.Clear();
        incomingReservations.Clear();
        if (restoredMaterials != null)
        {
            foreach (ResourceAmountSaveData item in restoredMaterials)
            {
                if (item != null && item.amount > 0)
                {
                    deliveredMaterials[(ResourceType)item.resourceType] = item.amount;
                }
            }
        }
        if (selectedCrop == null && savedState != PlanterState.Empty)
        {
            selectedCrop = settings != null ? settings.GetDefaultCrop() : null;
        }

        EnsureCropObject();
        if (selectedCrop != null) crop?.Bind(this, selectedCrop);

        switch (savedState)
        {
            case PlanterState.Ready:
                SetState(PlanterState.Ready, 0f);
                break;
            case PlanterState.Growing:
                SetState(
                    PlanterState.Growing,
                    Mathf.Max(0.01f, savedRemainingTime));
                break;
            case PlanterState.WaitingMaterials:
                SetState(PlanterState.WaitingMaterials, 0f);
                ReevaluateTasks();
                break;
            default:
                SetState(PlanterState.Empty, 0f);
                break;
        }
    }

    private void EnsureCropObject()
    {
        if (crop != null || settings == null)
        {
            return;
        }

        GameObject cropObject = new GameObject("PlanterCrop");
        cropObject.transform.SetParent(transform, false);
        cropObject.transform.localPosition = settings.CropLocalPosition;

        cropRenderer = cropObject.AddComponent<SpriteRenderer>();
        cropRenderer.sortingOrder = settings.CropOrderInGroup;
        BoxCollider2D collider = cropObject.AddComponent<BoxCollider2D>();
        collider.size = settings.InteractionSize;

        crop = cropObject.AddComponent<PlanterCrop>();
        FloraDefinitionSO initialCrop = selectedCrop ?? settings.GetDefaultCrop();
        crop.Bind(this, initialCrop);
    }

    private void ConfigureRendering()
    {
        if (settings == null) return;

        sortingGroup = GetComponent<SortingGroup>();
        if (sortingGroup == null)
        {
            sortingGroup = gameObject.AddComponent<SortingGroup>();
        }

        sortingGroup.sortingLayerName = settings.StructureSortingLayer;
        sortingGroup.sortingOrder = settings.StructureOrderInLayer;
    }

    private void SetState(PlanterState newState, float growthTime)
    {
        CancelInvoke(nameof(CompleteGrowth));
        state = newState;
        remainingGrowthTime = state == PlanterState.Growing
            ? Mathf.Max(0f, growthTime)
            : 0f;
        growthCompletesAt = state == PlanterState.Growing
            ? Time.time + remainingGrowthTime
            : 0f;

        if (state == PlanterState.Growing)
        {
            Invoke(nameof(CompleteGrowth), remainingGrowthTime);
        }

        EnsureCropObject();
        if (crop == null)
        {
            return;
        }

        crop.gameObject.SetActive(
            state == PlanterState.Growing || state == PlanterState.Ready);
        crop.SetReady(state == PlanterState.Ready);

        if (cropRenderer != null && settings != null)
        {
            cropRenderer.sprite = state == PlanterState.Ready
                ? settings.GetReadySprite(selectedCrop)
                : settings.GetGrowingSprite(selectedCrop);
        }

        StateChanged?.Invoke();
    }

    private void CompleteGrowth()
    {
        if (initialized && state == PlanterState.Growing)
        {
            SetState(PlanterState.Ready, 0f);
        }
    }

    private int GetRequiredAmount(ResourceType type)
    {
        if (selectedCrop?.plantingRequirements == null) return 0;
        int total = 0;
        foreach (PlantingRequirement requirement in selectedCrop.plantingRequirements)
        {
            if (requirement != null && requirement.resourceType == type)
            {
                total += Mathf.Max(1, requirement.amount);
            }
        }
        return total;
    }

    private int GetUnreservedNeed(ResourceType type)
    {
        return Mathf.Max(0, GetRequiredAmount(type)
            - GetAmount(deliveredMaterials, type)
            - GetAmount(incomingReservations, type));
    }

    private bool HasAllPlantingMaterials()
    {
        if (selectedCrop?.plantingRequirements == null
            || selectedCrop.plantingRequirements.Count == 0)
        {
            return true;
        }

        foreach (PlantingRequirement requirement in selectedCrop.plantingRequirements)
        {
            if (requirement != null
                && GetAmount(deliveredMaterials, requirement.resourceType)
                    < GetRequiredAmount(requirement.resourceType))
            {
                return false;
            }
        }
        return true;
    }

    private static int GetAmount(
        Dictionary<ResourceType, int> source,
        ResourceType type)
    {
        return source.TryGetValue(type, out int amount) ? amount : 0;
    }

    private void DropDeliveredMaterials()
    {
        foreach (KeyValuePair<ResourceType, int> entry in deliveredMaterials)
        {
            if (entry.Value > 0)
            {
                ItemSpawner.Instance?.SpawnResource(
                    entry.Key,
                    transform.position + Vector3.up * 0.5f,
                    entry.Value);
            }
        }
        deliveredMaterials.Clear();
    }
}
