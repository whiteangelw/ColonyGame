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
    private float totalExternalGrowthApplied;
    private float lastExternalGrowthApplied;
    private int externalGrowthApplicationCount;
    private string activeExternalEffectSource = string.Empty;
    private float activeExternalSpeedBonusPercent;
    private float externalEffectExpiresAt;
    private float externalEffectLastTickAt;
    private float externalEffectPulseInterval = 1f;
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
    public bool IsGrowing => initialized && state == PlanterState.Growing;
    public float RemainingGrowthTime => state == PlanterState.Growing
        ? Mathf.Max(0f, growthCompletesAt - Time.time)
        : 0f;
    public float TotalExternalGrowthApplied => totalExternalGrowthApplied;
    public float LastExternalGrowthApplied => lastExternalGrowthApplied;
    public int ExternalGrowthApplicationCount =>
        externalGrowthApplicationCount;
    public bool HasActiveExternalGrowthEffect => IsGrowing
        && activeExternalSpeedBonusPercent > 0f
        && Time.time < externalEffectExpiresAt;
    public string ActiveExternalEffectSource => HasActiveExternalGrowthEffect
        ? activeExternalEffectSource
        : string.Empty;
    public float ActiveExternalSpeedBonusPercent =>
        HasActiveExternalGrowthEffect ? activeExternalSpeedBonusPercent : 0f;
    public float ExternalGrowthEffectRemaining => HasActiveExternalGrowthEffect
        ? Mathf.Max(0f, externalEffectExpiresAt - Time.time)
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
        PlanterGrowthRegistry.Register(this);
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
        PlanterGrowthRegistry.Unregister(this);
        DropDeliveredMaterials();
        incomingReservations.Clear();
        initialized = false;
        CancelInvoke(nameof(CompleteGrowth));
        ClearExternalGrowthEffect();
        structure = null;
        if (crop != null) Destroy(crop.gameObject);
    }

    private void OnDisable()
    {
        PlanterGrowthRegistry.Unregister(this);
        CancelInvoke(nameof(CompleteGrowth));
        CancelInvoke(nameof(ProcessExternalGrowthEffect));
    }

    private void OnEnable()
    {
        if (initialized) PlanterGrowthRegistry.Register(this);
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

        if (HasActiveExternalGrowthEffect)
        {
            externalEffectLastTickAt = Time.time;
            ScheduleExternalGrowthEffectPulse();
        }
        else
        {
            ClearExternalGrowthEffect();
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

    // Ponto de extensão genérico. A plantadeira não conhece a origem do
    // efeito: criatura, fertilizante, clima ou máquina podem usá-lo.
    public bool ApplyExternalGrowthProgress(float growthSeconds)
    {
        if (!IsGrowing || growthSeconds <= 0f) return false;

        float appliedGrowth = Mathf.Min(growthSeconds, RemainingGrowthTime);
        if (appliedGrowth <= 0f) return false;

        growthCompletesAt -= appliedGrowth;
        lastExternalGrowthApplied = appliedGrowth;
        totalExternalGrowthApplied += appliedGrowth;
        externalGrowthApplicationCount++;
        remainingGrowthTime = Mathf.Max(0f, growthCompletesAt - Time.time);
        CancelInvoke(nameof(CompleteGrowth));

        if (remainingGrowthTime <= 0f)
        {
            CompleteGrowth();
        }
        else
        {
            Invoke(nameof(CompleteGrowth), remainingGrowthTime);
        }

        return true;
    }

    // Efeito genérico: a plantadeira não conhece criatura, fertilizante,
    // clima ou qualquer outro possível emissor.
    public bool ApplyTimedExternalGrowthSpeedEffect(
        string sourceId,
        float bonusPercent,
        float duration,
        float pulseInterval)
    {
        if (!IsGrowing || bonusPercent <= 0f || duration <= 0f)
            return false;

        bool alreadyActive = HasActiveExternalGrowthEffect;
        float clampedBonus = Mathf.Max(0f, bonusPercent);
        if (!alreadyActive || clampedBonus >= activeExternalSpeedBonusPercent)
        {
            activeExternalSpeedBonusPercent = clampedBonus;
            activeExternalEffectSource = string.IsNullOrWhiteSpace(sourceId)
                ? "external"
                : sourceId;
        }

        externalEffectExpiresAt = Mathf.Max(
            externalEffectExpiresAt,
            Time.time + Mathf.Max(0.1f, duration));
        externalEffectPulseInterval = Mathf.Max(0.25f, pulseInterval);

        if (!alreadyActive)
            externalEffectLastTickAt = Time.time;

        ScheduleExternalGrowthEffectPulse();
        StateChanged?.Invoke();
        return true;
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
        if (newState == PlanterState.Growing
            && state != PlanterState.Growing)
        {
            ClearExternalGrowthEffect();
            totalExternalGrowthApplied = 0f;
            lastExternalGrowthApplied = 0f;
            externalGrowthApplicationCount = 0;
        }
        state = newState;
        if (state != PlanterState.Growing)
            ClearExternalGrowthEffect();
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

    private void ProcessExternalGrowthEffect()
    {
        if (!IsGrowing || activeExternalSpeedBonusPercent <= 0f)
        {
            ClearExternalGrowthEffect();
            return;
        }

        float now = Time.time;
        float effectiveTickEnd = Mathf.Min(now, externalEffectExpiresAt);
        float elapsed = Mathf.Max(
            0f, effectiveTickEnd - externalEffectLastTickAt);
        float bonusPercent = activeExternalSpeedBonusPercent;
        externalEffectLastTickAt = effectiveTickEnd;
        float extraGrowth = elapsed * bonusPercent / 100f;
        ApplyExternalGrowthProgress(extraGrowth);

        if (IsGrowing && activeExternalSpeedBonusPercent > 0f
            && now < externalEffectExpiresAt)
            ScheduleExternalGrowthEffectPulse();
        else
            ClearExternalGrowthEffect();
    }

    private void ScheduleExternalGrowthEffectPulse()
    {
        CancelInvoke(nameof(ProcessExternalGrowthEffect));
        if (!HasActiveExternalGrowthEffect) return;

        float delay = Mathf.Min(
            externalEffectPulseInterval,
            Mathf.Max(0.01f, externalEffectExpiresAt - Time.time));
        Invoke(nameof(ProcessExternalGrowthEffect), delay);
    }

    private void ClearExternalGrowthEffect()
    {
        CancelInvoke(nameof(ProcessExternalGrowthEffect));
        activeExternalEffectSource = string.Empty;
        activeExternalSpeedBonusPercent = 0f;
        externalEffectExpiresAt = 0f;
        externalEffectLastTickAt = 0f;
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
