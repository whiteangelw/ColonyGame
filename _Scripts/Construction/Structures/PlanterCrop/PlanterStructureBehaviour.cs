using UnityEngine;
using UnityEngine.Rendering;

public enum PlanterState
{
    Empty = 0,
    Growing = 1,
    Ready = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(ConfiguredStructure))]
public sealed class PlanterStructureBehaviour : MonoBehaviour,
    IConfiguredStructureBehaviour,
    IInteractable
{
    [SerializeField] private PlanterSettingsSO settings;

    private ConfiguredStructure structure;
    private PlanterCrop crop;
    private SpriteRenderer cropRenderer;
    private SortingGroup sortingGroup;
    private PlanterState state = PlanterState.Empty;
    private float remainingGrowthTime;
    private bool initialized;
    private FloraDefinitionSO selectedCrop;

    public bool UsesSpecializedSaveData => false;
    public PlanterState State => state;
    public bool IsCropReady => initialized && state == PlanterState.Ready;
    public float RemainingGrowthTime => Mathf.Max(0f, remainingGrowthTime);
    public Vector2Int GridPosition => structure != null
        ? structure.GridPosition
        : GridManager.Instance != null
            ? GridManager.Instance.WorldToGridPosition(transform.position)
            : Vector2Int.zero;
    public string SelectedCropId => selectedCrop != null
        ? selectedCrop.floraId
        : string.Empty;
    public PlanterSettingsSO Settings => settings;

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
        initialized = false;
        structure = null;
        if (crop != null) Destroy(crop.gameObject);
    }

    private void Update()
    {
        if (!initialized || state != PlanterState.Growing)
        {
            return;
        }

        remainingGrowthTime = Mathf.Max(
            0f,
            remainingGrowthTime - Time.deltaTime);
        if (remainingGrowthTime <= 0f)
        {
            SetState(PlanterState.Ready, 0f);
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

        SetState(PlanterState.Growing, settings.GrowthDuration);
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
        EnsureCropObject();
        crop?.Bind(this, selectedCrop);
        BeginGrowth();
        return true;
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
            BeginGrowth();
        }
        else
        {
            SetState(PlanterState.Empty, 0f);
        }
    }

    public void RestoreState(
        PlanterState savedState,
        float savedRemainingTime,
        string savedCropId)
    {
        if (!initialized)
        {
            return;
        }

        selectedCrop = FloraManager.Instance?.GetDefinitionById(savedCropId);
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
        state = newState;
        remainingGrowthTime = state == PlanterState.Growing
            ? Mathf.Max(0f, growthTime)
            : 0f;

        EnsureCropObject();
        if (crop == null)
        {
            return;
        }

        crop.gameObject.SetActive(state != PlanterState.Empty);
        crop.SetReady(state == PlanterState.Ready);

        if (cropRenderer != null && settings != null)
        {
            cropRenderer.sprite = state == PlanterState.Ready
                ? settings.ReadySprite
                : settings.GrowingSprite;
        }
    }
}
