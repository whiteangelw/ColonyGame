using UnityEngine;

public class PrintingPod : MonoBehaviour, IDismantlable
{
    public static PrintingPod Instance { get; private set; }

    [Header("Estado")]
    [SerializeField] private bool isOperational;

    [Header("Entrega de recompensas")]
    [Tooltip("Ponto desejado para os recursos. Coloque-o fora da estrutura, junto ao chão.")]
    [SerializeField] private Transform rewardDropPoint;

    [Tooltip("Distância máxima, em células, para procurar um piso válido ao redor do ponto de entrega.")]
    [SerializeField, Min(1)] private int rewardDropSearchRadius = 6;

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

    public bool TryGetRewardDropPosition(out Vector3 worldPosition)
    {
        if (gridManager == null)
        {
            gridManager = GridManager.Instance != null
                ? GridManager.Instance
                : FindFirstObjectByType<GridManager>();
        }

        if (gridManager == null)
        {
            worldPosition = transform.position;
            return false;
        }

        UpdateGridPosition();

        Vector2Int desiredPosition = rewardDropPoint != null
            ? gridManager.WorldToGridPosition(rewardDropPoint.position)
            : gridPosition + Vector2Int.right * 2;

        if (TryFindStandableDropCell(desiredPosition, out Vector2Int dropCell))
        {
            worldPosition = gridManager.GridToWorldPosition(dropCell);
            return true;
        }

        worldPosition = transform.position;
        return false;
    }

    private bool TryFindStandableDropCell(
        Vector2Int center,
        out Vector2Int result)
    {
        int maxRadius = Mathf.Max(1, rewardDropSearchRadius);

        // Procura em anéis de distância Manhattan. Assim, o ponto indicado
        // no prefab continua sendo a preferência, mas nunca aceitamos uma
        // célula sem apoio ou a própria célula ocupada pela máquina.
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int deltaX = -radius; deltaX <= radius; deltaX++)
            {
                int deltaY = radius - Mathf.Abs(deltaX);

                Vector2Int upperCandidate =
                    center + new Vector2Int(deltaX, deltaY);

                if (IsValidDropCell(upperCandidate))
                {
                    result = upperCandidate;
                    return true;
                }

                if (deltaY == 0)
                {
                    continue;
                }

                Vector2Int lowerCandidate =
                    center + new Vector2Int(deltaX, -deltaY);

                if (IsValidDropCell(lowerCandidate))
                {
                    result = lowerCandidate;
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private bool IsValidDropCell(Vector2Int position)
    {
        return position != gridPosition
            && gridManager.IsStandable(position.x, position.y);
    }

    public void Dismantle()
    {
        WorldInteractionService.Instance?.DismantleTile(
            gridPosition.x,
            gridPosition.y
        );
    }
}
