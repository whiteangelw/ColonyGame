using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantController))]
public sealed class DuplicantIntentSensor : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float observationInterval = 0.15f;

    public event Action<DuplicantIntentSignal> IntentRaised;
    public DuplicantIntentType LastIntent { get; private set; }

    private DuplicantController controller;
    private DuplicantTaskRunner runner;
    private DuplicantBrain brain;
    private DuplicantVitals vitals;

    private Task lastTask;
    private MealPlan lastMealPlan;
    private BedStructureBehaviour lastBed;
    private DuplicantController.WorkerState lastWorkerState;
    private float lastFailureTime;
    private bool wasHungry;
    private bool wasStarving;
    private bool wasTired;
    private bool wasBedResting;
    private bool wasEmergencyResting;
    private float nextObservationTime;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
        runner = GetComponent<DuplicantTaskRunner>();
        brain = GetComponent<DuplicantBrain>();
        vitals = GetComponent<DuplicantVitals>();
        CaptureCurrentState();
    }

    private void OnEnable()
    {
        nextObservationTime = Time.time;
    }

    private void Update()
    {
        if (SaveGameRuntime.IsLoading
            || Time.time < nextObservationTime)
        {
            return;
        }

        nextObservationTime = Time.time
            + Mathf.Max(0.05f, observationInterval);

        DuplicantIntentType detected = DetectHighestPriorityChange();
        CaptureCurrentState();

        if (detected != DuplicantIntentType.None)
        {
            Raise(detected);
        }
    }

    private DuplicantIntentType DetectHighestPriorityChange()
    {
        bool emergencyResting = vitals != null
            && vitals.IsEmergencyResting;
        bool bedResting = vitals != null && vitals.IsBedResting;

        if (emergencyResting && !wasEmergencyResting)
        {
            return DuplicantIntentType.EmergencyRest;
        }

        if (bedResting && !wasBedResting)
        {
            return DuplicantIntentType.Sleep;
        }

        if (controller.currentState == DuplicantController.WorkerState.Eating
            && lastWorkerState != DuplicantController.WorkerState.Eating)
        {
            return DuplicantIntentType.Eat;
        }

        BedStructureBehaviour currentBed = runner != null
            ? runner.ActiveBed
            : null;
        if (currentBed != null && currentBed != lastBed)
        {
            return DuplicantIntentType.SeekBed;
        }

        MealPlan currentMealPlan = brain != null
            ? brain.CurrentMealPlan
            : null;
        if (currentMealPlan != null && currentMealPlan != lastMealPlan)
        {
            return DuplicantIntentType.SeekFood;
        }

        Task currentTask = controller.currentTask;
        if (currentTask != null && currentTask != lastTask)
        {
            return FromTaskType(currentTask.type);
        }

        if (runner != null
            && runner.LastFailureTime >= 0f
            && runner.LastFailureTime > lastFailureTime)
        {
            return DuplicantIntentType.WorkFailed;
        }

        bool starving = vitals != null && vitals.IsStarving;
        if (starving && !wasStarving) return DuplicantIntentType.Starving;

        bool hungry = vitals != null && vitals.IsHungry;
        if (hungry && !wasHungry) return DuplicantIntentType.Hungry;

        bool tired = vitals != null && vitals.IsTired;
        if (tired && !wasTired) return DuplicantIntentType.Tired;

        return DuplicantIntentType.None;
    }

    private void CaptureCurrentState()
    {
        if (controller == null) return;

        lastTask = controller.currentTask;
        lastWorkerState = controller.currentState;
        lastMealPlan = brain != null ? brain.CurrentMealPlan : null;
        lastBed = runner != null ? runner.ActiveBed : null;
        lastFailureTime = runner != null ? runner.LastFailureTime : -1f;
        wasHungry = vitals != null && vitals.IsHungry;
        wasStarving = vitals != null && vitals.IsStarving;
        wasTired = vitals != null && vitals.IsTired;
        wasBedResting = vitals != null && vitals.IsBedResting;
        wasEmergencyResting = vitals != null
            && vitals.IsEmergencyResting;
    }

    private void Raise(DuplicantIntentType type)
    {
        LastIntent = type;
        string details = type == DuplicantIntentType.WorkFailed
            && runner != null
                ? runner.LastFailureDetails
                : string.Empty;
        IntentRaised?.Invoke(new DuplicantIntentSignal(type, details));
    }

    private static DuplicantIntentType FromTaskType(TaskType type)
    {
        switch (type)
        {
            case TaskType.Dig: return DuplicantIntentType.Dig;
            case TaskType.BuildTile: return DuplicantIntentType.Build;
            case TaskType.HaulResource: return DuplicantIntentType.Haul;
            case TaskType.Dismantle: return DuplicantIntentType.Dismantle;
            case TaskType.Harvest: return DuplicantIntentType.Harvest;
            default: return DuplicantIntentType.None;
        }
    }
}
