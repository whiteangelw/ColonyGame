using UnityEngine;

[RequireComponent(typeof(CreatureBrain), typeof(CreatureMovement), typeof(CreaturePerception))]
[RequireComponent(typeof(CreatureFeeding))]
[RequireComponent(typeof(CreatureDomestication), typeof(CreatureDirectFeedingTarget))]
[RequireComponent(typeof(CreaturePresentation))]
[RequireComponent(typeof(CreatureHabitatLink))]
[RequireComponent(typeof(CreatureTerritorialGardener))]
public sealed class CreatureController : MonoBehaviour, IInteractable
{
    [SerializeField] private CreatureDefinitionSO definition;

    public CreatureDefinitionSO Definition => definition;
    public CreatureState State { get; private set; } = CreatureState.Idle;
    public Vector2Int GridPosition { get; private set; }
    public Vector2Int HomePosition { get; private set; }
    public Vector2Int NaturalHomePosition { get; private set; }
    public string LastDecisionReason { get; private set; } = "Aguardando primeira decisão";
    public Vector2Int? CurrentTarget { get; private set; }
    public CreatureMovement Movement { get; private set; }
    public CreatureBrain Brain { get; private set; }
    public CreatureFeeding Feeding { get; private set; }
    public CreatureDomestication Domestication { get; private set; }
    public CreatureDirectFeedingTarget DirectFeeding { get; private set; }
    public CreaturePresentation Presentation { get; private set; }
    public CreatureHabitatLink HabitatLink { get; private set; }
    public CreatureTerritorialGardener TerritorialGardener { get; private set; }
    public bool IsInitialized { get; private set; }
    public string SpawnOrigin { get; private set; } = "Manual";
    public string SpawnBiomeId { get; private set; } = string.Empty;

    private Coroutine initializationRoutine;

    private void Awake()
    {
        Movement = GetComponent<CreatureMovement>();
        Brain = GetComponent<CreatureBrain>();
        Feeding = GetComponent<CreatureFeeding>();
        Domestication = GetComponent<CreatureDomestication>();
        DirectFeeding = GetComponent<CreatureDirectFeedingTarget>();
        Presentation = GetComponent<CreaturePresentation>();
        HabitatLink = GetComponent<CreatureHabitatLink>();
        TerritorialGardener = GetComponent<CreatureTerritorialGardener>();
    }

    private void Start()
    {
        if (definition != null && definition.sprite != null)
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.sprite = definition.sprite;
        }

        if (!IsInitialized && initializationRoutine == null)
            initializationRoutine = StartCoroutine(InitializeManualRoutine());
    }

    private void OnEnable() => CreatureRegistry.Register(this);
    private void OnDisable() => CreatureRegistry.Unregister(this);

    public void RefreshGridPosition()
    {
        if (GridManager.Instance != null)
            GridPosition = GridManager.Instance.WorldToGridPosition(transform.position);
    }

    public void SetDecision(CreatureState state, string reason, Vector2Int? target = null)
    {
        State = state;
        LastDecisionReason = reason;
        CurrentTarget = target;
    }

    public void ClearTarget() => CurrentTarget = null;

    public void SetHomePosition(Vector2Int position)
    {
        HomePosition = position;
    }

    public void SetNaturalHomePosition(Vector2Int position)
    {
        NaturalHomePosition = position;
    }

    public void RestoreNaturalHome()
    {
        HomePosition = NaturalHomePosition;
    }

    public void RelocateTo(Vector2Int position)
    {
        Movement?.CancelMovement();
        GridPosition = position;
        transform.position = GridManager.Instance.GridToWorldPosition(position);
    }

    public void OnInteract()
    {
        CreatureInspectUI.Instance?.Open(this);
    }
    private void OnMouseDown()
    {
        OnInteract();
    }


    public void InitializeSpawn(
        CreatureDefinitionSO spawnedDefinition,
        Vector2Int position,
        string origin,
        string biomeId)
    {
        if (initializationRoutine != null)
        {
            StopCoroutine(initializationRoutine);
            initializationRoutine = null;
        }

        if (spawnedDefinition != null) definition = spawnedDefinition;
        SpawnOrigin = string.IsNullOrWhiteSpace(origin) ? "Runtime" : origin;
        SpawnBiomeId = biomeId ?? string.Empty;
        ApplyInitializedPosition(position);
    }

    public void InitializeRestored(
        CreatureDefinitionSO restoredDefinition,
        Vector2Int position,
        Vector2Int homePosition,
        string origin,
        string biomeId)
    {
        InitializeSpawn(restoredDefinition, position, origin, biomeId);
        HomePosition = homePosition;
        SetDecision(CreatureState.Idle, "Estado restaurado do save");
    }

    private System.Collections.IEnumerator InitializeManualRoutine()
    {
        while (GridManager.Instance == null
            || !GridManager.Instance.IsGridReady
            || NavGraphGenerator.Instance == null
            || !NavGraphGenerator.Instance.IsGraphReady)
        {
            yield return null;
        }

        Vector2Int requested = GridManager.Instance.WorldToGridPosition(
            transform.position);
        Vector2Int safe = NavGraphGenerator.Instance.IsNavigablePosition(
            requested.x, requested.y)
            ? requested
            : GridSafetyUtility.FindNearestStandableTileBFS(requested);
        ApplyInitializedPosition(safe);
        initializationRoutine = null;
    }

    private void ApplyInitializedPosition(Vector2Int position)
    {
        GridPosition = position;
        HomePosition = position;
        NaturalHomePosition = position;
        transform.position = GridManager.Instance.GridToWorldPosition(position);
        IsInitialized = true;
        SetDecision(CreatureState.Idle, "Inicialização concluída");
        Brain?.ForceDecision();
    }

    private void OnDrawGizmosSelected()
    {
        if (definition == null || GridManager.Instance == null || !IsInitialized) return;
        float cell = GridManager.Instance.cellSize;
        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, definition.territoryRadius * cell);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, definition.attractionRadius * cell);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, definition.avoidanceRadius * cell);
    }
}
