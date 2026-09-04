using UnityEngine;

[RequireComponent(typeof(DuplicantMovement))]
[RequireComponent(typeof(DuplicantTaskRunner))]
[RequireComponent(typeof(DuplicantBrain))]
[RequireComponent(typeof(DuplicantVitals))]
public class DuplicantController : MonoBehaviour
{
    public enum WorkerState { Idle, Moving, Working, Falling, Resting }

    [Header("Referências")]
    public DuplicantCapabilityProfile capabilityProfile;

    [Tooltip("Define afinidades de trabalho. Sem perfil, este duplicant é neutro.")]
    public DuplicantWorkProfile workProfile;

    [Tooltip("Define metabolismo e recuperação. Sem perfil, usa valores neutros.")]
    public DuplicantLifeProfile lifeProfile;

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
    public DuplicantVitals Vitals { get; private set; }

    private bool isRecovering;

    public int GetWorkAffinity(TaskType taskType)
    {
        return workProfile != null
            ? workProfile.GetAffinity(taskType)
            : 0;
    }

    private void Awake()
    {
        Movement = GetComponent<DuplicantMovement>();
        TaskRunner = GetComponent<DuplicantTaskRunner>();
        Brain = GetComponent<DuplicantBrain>();
        EnsureVitals();
    }

    public DuplicantVitals EnsureVitals()
    {
        if (Vitals == null)
        {
            Vitals = GetComponent<DuplicantVitals>();
        }

        if (Vitals == null)
        {
            Vitals = gameObject.AddComponent<DuplicantVitals>();
        }

        return Vitals;
    }

    private void OnEnable()
    {
        ReachabilityManager.Instance?.RegisterDuplicant(this);
    }

    private void OnDisable()
    {
        ReachabilityManager.Instance?.UnregisterDuplicant(this);
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

    public void RestoreAt(Vector2Int restoredGridPosition)
    {
        currentTask = null;
        currentState = WorkerState.Idle;
        gridPosition = restoredGridPosition;
        Movement?.SnapToGrid(restoredGridPosition);
        Brain?.RequestImmediateTaskSearch();
    }
}
