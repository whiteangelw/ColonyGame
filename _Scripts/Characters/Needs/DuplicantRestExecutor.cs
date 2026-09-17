using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BedRestResult
{
    None,
    Completed,
    NoReachableBed,
    BedBecameUnavailable,
    InterruptedByHunger,
    Cancelled
}

public sealed class DuplicantRestExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantVitals vitals;
    private readonly DuplicantPathFollower pathFollower;
    private BedStructureBehaviour activeBed;
    private bool cancelled;

    public BedRestResult Result { get; private set; }
    public BedStructureBehaviour ActiveBed => activeBed;

    public DuplicantRestExecutor(
        DuplicantController controller,
        DuplicantVitals vitals,
        DuplicantPathFollower pathFollower)
    {
        this.controller = controller;
        this.vitals = vitals;
        this.pathFollower = pathFollower;
    }

    public IEnumerator Execute()
    {
        cancelled = false;
        Result = BedRestResult.None;
        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;

        if (settings == null
            || !BedRegistry.TryReserveBest(
                controller,
                settings,
                out activeBed,
                out List<Vector2Int> path))
        {
            Result = BedRestResult.NoReachableBed;
            yield break;
        }

        yield return pathFollower.FollowInteraction(
            activeBed.GridPosition,
            path);

        if (cancelled)
        {
            Cleanup();
            yield break;
        }

        if (!pathFollower.Succeeded
            || activeBed == null
            || !activeBed.TryOccupy(controller))
        {
            Result = BedRestResult.BedBecameUnavailable;
            Cleanup();
            yield break;
        }

        controller.currentState = DuplicantController.WorkerState.Resting;
        vitals.BeginBedRest();
        GameEvents.TriggerFloatingTextRequested(
            "Dormindo",
            controller.transform.position,
            Color.cyan);

        float elapsed = 0f;
        float minimumDuration = Mathf.Max(0f, settings.minimumBedRestDuration);
        float wakeTarget = Mathf.Clamp01(settings.wakeUpAtEnergyPercent);

        while (!cancelled
            && activeBed != null
            && activeBed.IsUsableBy(controller)
            && (elapsed < minimumDuration
                || vitals.EnergyPercent < wakeTarget))
        {
            if (vitals.IsStarving && elapsed >= minimumDuration)
            {
                Result = BedRestResult.InterruptedByHunger;
                break;
            }

            float delta = Time.deltaTime;
            elapsed += delta;
            vitals.RestoreEnergy(settings.bedEnergyRecoveryPerSecond * delta);
            yield return null;
        }

        if (cancelled)
        {
            Result = BedRestResult.Cancelled;
        }
        else if (activeBed == null || !activeBed.IsUsableBy(controller))
        {
            Result = BedRestResult.BedBecameUnavailable;
        }
        else if (Result == BedRestResult.None)
        {
            Result = BedRestResult.Completed;
        }

        Cleanup();
    }

    public void Cancel()
    {
        cancelled = true;
        if (Result == BedRestResult.None) Result = BedRestResult.Cancelled;
        Cleanup();
    }

    private void Cleanup()
    {
        vitals?.EndBedRest();
        if (activeBed != null) activeBed.Release(controller);
        activeBed = null;
        if (controller != null
            && controller.currentState == DuplicantController.WorkerState.Resting)
        {
            controller.currentState = DuplicantController.WorkerState.Idle;
        }
    }
}
