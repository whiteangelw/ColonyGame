using UnityEngine;

public class PrintingPod : MonoBehaviour, IDismantlable
{
    public static PrintingPod Instance { get; private set; }

    [Header("Estado")]
    [SerializeField] private bool isOperational;

    public bool IsOperational => isOperational;
    public Vector2Int GridPosition => gridPosition;

    public Vector2Int gridPosition;
    private GridManager gridManager;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // O prefab nunca nasce ativo antes de entrar no mundo construído.
        isOperational = false;
    }

    private void Start()
    {
        gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        UpdateGridPosition();

        SetOperational(true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (isOperational)
        {
            isOperational = false;
            GameEvents.TriggerPrintingPodRemoved(this);
        }
    }

    public void UpdateGridPosition()
    {
        if (gridManager == null) gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        if (gridManager != null)
        {
            gridPosition = gridManager.WorldToGridPosition(transform.position);
        }
    }

    public void SetOperational(bool operational)
    {
        if (isOperational == operational)
        {
            return;
        }

        isOperational = operational;

        if (isOperational)
        {
            UpdateGridPosition();
            GameEvents.TriggerPrintingPodBuilt(this);
        }
        else
        {
            GameEvents.TriggerPrintingPodRemoved(this);
        }
    }

    public void SetGridPosition(Vector2Int position)
    {
        gridPosition = position;
    }

    public void Dismantle()
    {
        WorldInteractionService.Instance?.DismantleTile(
            gridPosition.x,
            gridPosition.y
        );
    }
}
