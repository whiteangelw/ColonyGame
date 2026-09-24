using UnityEngine;

public enum DuplicantAttentionReason
{
    None = 0,
    Tired = 1,
    Hungry = 2,
    Exhausted = 3,
    Starving = 4,
    EmergencyResting = 5
}

public readonly struct DuplicantAttentionReport
{
    public DuplicantAttentionSource Source { get; }
    public DuplicantAttentionReason Reason { get; }
    public float Severity { get; }

    public DuplicantAttentionReport(
        DuplicantAttentionSource source,
        DuplicantAttentionReason reason,
        float severity)
    {
        Source = source;
        Reason = reason;
        Severity = Mathf.Clamp01(severity);
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantVitals))]
[RequireComponent(typeof(DuplicantSelectionTarget))]
public sealed class DuplicantAttentionSource : MonoBehaviour
{
    [Header("Alertas não críticos")]
    [Tooltip("Ative somente se quiser alertar antes da fome crítica.")]
    [SerializeField] private bool includeHungry;
    [Tooltip("Ative somente se quiser alertar antes da energia crítica.")]
    [SerializeField] private bool includeTired;

    public Transform TargetTransform => transform;
    public DuplicantSelectionTarget SelectionTarget { get; private set; }
    public string DisplayName => gameObject.name;

    private DuplicantVitals vitals;
    private DuplicantAttentionService registeredService;

    private void Awake()
    {
        vitals = GetComponent<DuplicantVitals>();
        SelectionTarget = GetComponent<DuplicantSelectionTarget>();
    }

    private void OnEnable()
    {
        if (vitals == null) vitals = GetComponent<DuplicantVitals>();
        if (SelectionTarget == null)
            SelectionTarget = GetComponent<DuplicantSelectionTarget>();

        vitals.OnVitalsChanged += HandleVitalsChanged;
        TryRegister();
    }

    private void Start()
    {
        // Cobre o caso em que o serviço inicia depois do prefab.
        TryRegister();
    }

    private void OnDisable()
    {
        if (vitals != null)
            vitals.OnVitalsChanged -= HandleVitalsChanged;

        if (registeredService != null)
            registeredService.Unregister(this);

        registeredService = null;
    }

    public DuplicantAttentionReport BuildReport()
    {
        if (vitals == null)
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.None,
                0f);

        if (vitals.IsEmergencyResting)
        {
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.EmergencyResting,
                1f);
        }

        if (vitals.IsStarving)
        {
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.Starving,
                1f - vitals.HungerPercent);
        }

        if (vitals.IsExhausted)
        {
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.Exhausted,
                1f - vitals.EnergyPercent);
        }

        if (includeHungry && vitals.IsHungry)
        {
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.Hungry,
                1f - vitals.HungerPercent);
        }

        if (includeTired && vitals.IsTired)
        {
            return new DuplicantAttentionReport(
                this,
                DuplicantAttentionReason.Tired,
                1f - vitals.EnergyPercent);
        }

        return new DuplicantAttentionReport(
            this,
            DuplicantAttentionReason.None,
            0f);
    }

    public void RefreshAttention()
    {
        TryRegister();
        registeredService?.Refresh(this);
    }

    private void HandleVitalsChanged(DuplicantVitals changedVitals)
    {
        if (changedVitals == vitals)
            RefreshAttention();
    }

    private void TryRegister()
    {
        DuplicantAttentionService service =
            DuplicantAttentionService.Instance;

        if (service == null || registeredService == service) return;

        if (registeredService != null)
            registeredService.Unregister(this);

        registeredService = service;
        registeredService.Register(this);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            RefreshAttention();
    }
}
