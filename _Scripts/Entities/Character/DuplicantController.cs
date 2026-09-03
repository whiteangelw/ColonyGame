using UnityEngine;

[RequireComponent(typeof(DuplicantMovement))]
[RequireComponent(typeof(DuplicantTaskRunner))]
[RequireComponent(typeof(DuplicantBrain))]
public class DuplicantController : MonoBehaviour
{
    public enum WorkerState { Idle, Moving, Working, Falling }

    [Header("Referências")]
    public DuplicantCapabilityProfile capabilityProfile;

    [Header("Configurações do Colono")]
    public float baseMoveSpeed = 4f;
    public float fallSpeed = 8f;
    public float workDuration = 1f;

    [Header("Estado Atual")]
    public WorkerState currentState = WorkerState.Idle;
    public Vector2Int gridPosition;
    public Task currentTask;

    // Subcomponentes Desacoplados
    public DuplicantMovement Movement { get; private set; }
    public DuplicantTaskRunner TaskRunner { get; private set; }
    public DuplicantBrain Brain { get; private set; }

    private bool isRecovering;

    private void Awake()
    {
        Movement = GetComponent<DuplicantMovement>();
        TaskRunner = GetComponent<DuplicantTaskRunner>();
        Brain = GetComponent<DuplicantBrain>();
    }

    private void Start()
    {
        if (GridManager.Instance != null)
        {
            gridPosition = GridManager.Instance.WorldToGridPosition(transform.position);
            Movement.SnapToGrid(gridPosition);
        }
    }

    private void Update()
    {
        if (GridManager.Instance == null) return;

        Vector2Int visualGridPosition =
            GridManager.Instance.WorldToGridPosition(transform.position);

        FogOfWarManager.Instance?.RevealArea(visualGridPosition);
        CheckAndRescueIfTrapped(visualGridPosition);

        // Queda em Idle caso o chão desapareça
        if (currentState == WorkerState.Idle && currentTask == null && Movement.ShouldFall())
        {
            StartCoroutine(Movement.HandleFallingRoutine());
        }
    }
    /// <summary>
    /// Verifica se o colono ficou soterrado/preso e teleporta para o tile vazio mais próximo.
    /// </summary>
    private void CheckAndRescueIfTrapped(Vector2Int visualGridPosition)
    {
        if (GridManager.Instance == null || isRecovering)
        {
            return;
        }

        // Durante a interpolação, o Transform pode atravessar visualmente uma
        // célula sólida. Isso não significa que o duplicant está preso.
        if (currentState == WorkerState.Moving
            || currentState == WorkerState.Falling)
        {
            return;
        }

        if (!GridManager.Instance.IsPassable(
                visualGridPosition.x,
                visualGridPosition.y))
        {
            Vector2Int safeTile =
                GridSafetyUtility.FindNearestStandableTileBFS(
                    visualGridPosition
                );

            RecoverTo(safeTile);
        }
    }

    public void RecoverTo(Vector2Int safeTile)
    {
        if (isRecovering || GridManager.Instance == null)
        {
            return;
        }

        isRecovering = true;

        CancelCurrentTaskExecution();

        Movement?.SnapToGrid(safeTile);
        Brain?.RequestImmediateTaskSearch();

        isRecovering = false;

        if (Movement != null && Movement.ShouldFall())
        {
            StartCoroutine(Movement.HandleFallingRoutine());
        }
    }

    public void CancelCurrentTaskExecution()
    {
        Task interruptedTask = currentTask;

        Brain?.CancelActiveTaskExecution();
        TaskRunner?.CancelActiveTaskState();

        if (interruptedTask != null)
        {
            TaskManager.Instance?.ReleaseTask(interruptedTask);
        }

        currentTask = null;
        currentState = WorkerState.Idle;
        Brain?.RequestImmediateTaskSearch();
    }
}
