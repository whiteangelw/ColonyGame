using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class LifeCycleSystem : MonoBehaviour
{
    private static readonly ProfilerMarker ProcessNeedsTicksMarker =
        new ProfilerMarker("Colony.AI.ProcessNeedsTicks");
    public static LifeCycleSystem Instance { get; private set; }

    [SerializeField] private LifeCycleSettingsSO settings;

    public LifeCycleSettingsSO Settings => settings;
    public int RegisteredDuplicantCount => registeredVitals.Count;
    public int PendingTickCount => pendingTicks.Count;

    private readonly List<DuplicantVitals> registeredVitals =
        new List<DuplicantVitals>();
    private readonly Queue<DuplicantVitals> pendingTicks =
        new Queue<DuplicantVitals>();
    private float tickTimer;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        DuplicantVitals[] existing = FindObjectsByType<DuplicantVitals>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
        PerformanceMetricsService.RecordGlobalObjectSearch();

        foreach (DuplicantVitals vitals in existing)
        {
            Register(vitals);
        }
    }

    private void Update()
    {
        if (settings == null || SaveGameRuntime.IsLoading)
        {
            return;
        }

        tickTimer += Time.deltaTime;

        if (tickTimer >= settings.tickInterval && pendingTicks.Count == 0)
        {
            tickTimer -= settings.tickInterval;
            QueueNextTick();
        }

        ProcessTickBudget();
    }

    public void Register(DuplicantVitals vitals)
    {
        if (vitals == null || registeredVitals.Contains(vitals))
        {
            return;
        }

        registeredVitals.Add(vitals);
        vitals.Initialize(vitals.GetComponent<DuplicantController>()?.lifeProfile, settings);
    }

    public void Unregister(DuplicantVitals vitals)
    {
        if (vitals != null)
        {
            registeredVitals.Remove(vitals);
        }
    }

    private void QueueNextTick()
    {
        for (int i = registeredVitals.Count - 1; i >= 0; i--)
        {
            DuplicantVitals vitals = registeredVitals[i];

            if (vitals == null)
            {
                registeredVitals.RemoveAt(i);
                continue;
            }

            if (vitals.isActiveAndEnabled)
            {
                pendingTicks.Enqueue(vitals);
            }
        }
    }

    private void ProcessTickBudget()
    {
        using (ProcessNeedsTicksMarker.Auto())
        {
        int budget = Mathf.Max(1, settings.maximumDuplicantsPerFrame);

        while (budget > 0 && pendingTicks.Count > 0)
        {
            DuplicantVitals vitals = pendingTicks.Dequeue();

            if (vitals != null && vitals.isActiveAndEnabled)
            {
                vitals.ApplyTick(settings, settings.tickInterval);
                PerformanceMetricsService.RecordNeedsTick();
            }

            budget--;
        }
        }
    }
}
