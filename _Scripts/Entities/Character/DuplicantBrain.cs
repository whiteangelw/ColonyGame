using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DuplicantController))]
public class DuplicantBrain : MonoBehaviour
{
    private DuplicantController controller;
    private DuplicantMovement movement;
    private DuplicantTaskRunner taskRunner;
    private DuplicantVitals vitals;

    private float searchCooldown = 0f;
    private Coroutine brainCoroutine;
    private Coroutine activeTaskCoroutine;
    private Coroutine emergencyRestCoroutine;
    private Coroutine emergencyFoodCoroutine;
    private float nextPreventiveFoodSearchTime;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
        movement = GetComponent<DuplicantMovement>();
        taskRunner = GetComponent<DuplicantTaskRunner>();
        vitals = GetComponent<DuplicantVitals>();
    }

    private void OnEnable()
    {
        if (vitals != null)
        {
            vitals.OnEmergencyRestRequested += HandleEmergencyRestRequested;
        }

        if (brainCoroutine == null)
        {
            brainCoroutine = StartCoroutine(WorkerBrainRoutine());
        }
    }

    private void OnDisable()
    {
        if (vitals != null)
        {
            vitals.OnEmergencyRestRequested -= HandleEmergencyRestRequested;
        }

        if (emergencyRestCoroutine != null)
        {
            StopCoroutine(emergencyRestCoroutine);
            emergencyRestCoroutine = null;
            vitals?.CancelEmergencyRest();
        }

        if (activeTaskCoroutine != null)
        {
            StopCoroutine(activeTaskCoroutine);
        }

        if (emergencyFoodCoroutine != null)
        {
            StopCoroutine(emergencyFoodCoroutine);
            emergencyFoodCoroutine = null;
            taskRunner?.CancelEmergencyFoodState();
        }

        if (brainCoroutine != null)
        {
            StopCoroutine(brainCoroutine);
        }

        brainCoroutine = null;
        activeTaskCoroutine = null;
    }

    /// <summary>
    /// Loop principal de inteligência e tomada de decisão do colono.
    /// </summary>
    private IEnumerator WorkerBrainRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0.0f, 0.5f));
        WaitForSeconds waitInterval = new WaitForSeconds(0.3f);

        while (true)
        {
            yield return waitInterval;

            if (movement.ShouldFall() && controller.currentState != DuplicantController.WorkerState.Falling)
            {
                yield return StartCoroutine(movement.HandleFallingRoutine());
                continue;
            }

            if (searchCooldown > 0f)
            {
                searchCooldown -= 0.3f;
                continue;
            }

            if (vitals != null
                && (vitals.IsHungry || vitals.IsStarving)
                && !vitals.IsEmergencyResting
                && emergencyRestCoroutine == null
                && emergencyFoodCoroutine == null
                && controller.currentState == DuplicantController.WorkerState.Idle
                && Time.time >= nextPreventiveFoodSearchTime)
            {
                bool allowEmergencySources = vitals.CurrentHunger <= 0f;
                if (TryStartFoodRoutine(allowEmergencySources)) continue;
            }

            if (controller.currentState == DuplicantController.WorkerState.Idle && controller.currentTask == null && !movement.ShouldFall())
            {
                if (TaskManager.Instance != null)
                {
                    if (!TaskManager.Instance.TryAcquireTaskSearchSlot())
                    {
                        continue;
                    }

                    List<Vector2Int> calculatedPath;
                    Task availableTask = TaskManager.Instance.GetNextTaskFor(
                        controller.gridPosition,
                        out calculatedPath,
                        controller.capabilityProfile,
                        controller.workProfile
                    );

                    if (availableTask != null)
                    {
                        controller.currentTask = availableTask;

                        activeTaskCoroutine = StartCoroutine(
                            ExecuteAssignedTask(
                                controller.currentTask,
                                calculatedPath
                            )
                        );
                    }
                    else
                    {
                        searchCooldown = 1.0f;
                    }
                }
            }
        }
    }

    private IEnumerator ExecuteAssignedTask(
        Task task,
        List<Vector2Int> calculatedPath)
    {
        yield return taskRunner.ExecuteTaskRoutine(task, calculatedPath);
        activeTaskCoroutine = null;
    }

    /// <summary>
    /// Permite definir um cooldown externo para busca de tarefas.
    /// </summary>
    public void SetSearchCooldown(float time)
    {
        searchCooldown = time;
    }

    public void CancelActiveTaskExecution()
    {
        if (activeTaskCoroutine == null)
        {
            return;
        }

        StopCoroutine(activeTaskCoroutine);
        activeTaskCoroutine = null;
        searchCooldown = 0f;
    }

    public void RequestImmediateTaskSearch()
    {
        searchCooldown = 0f;
    }

    private void HandleEmergencyRestRequested(DuplicantVitals source)
    {
        if (emergencyRestCoroutine != null || !isActiveAndEnabled)
        {
            return;
        }

        if (emergencyFoodCoroutine != null)
        {
            StopCoroutine(emergencyFoodCoroutine);
            emergencyFoodCoroutine = null;
            taskRunner.CancelEmergencyFoodState();
        }

        controller.CancelCurrentTaskExecution();
        emergencyRestCoroutine = StartCoroutine(EmergencyRestRoutine());
    }

    private IEnumerator EmergencyRestRoutine()
    {
        vitals.BeginEmergencyRest();
        controller.currentState = DuplicantController.WorkerState.Resting;

        GameEvents.TriggerFloatingTextRequested(
            "Soneca de emergência",
            controller.transform.position,
            Color.cyan
        );

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        float duration = settings != null
            ? settings.emergencyNapDuration
            : 5f;
        float recovery = settings != null
            ? settings.emergencyNapRecoveryPercent
            : 0.20f;

        yield return new WaitForSeconds(duration);

        vitals.CompleteEmergencyRest(recovery);
        controller.currentState = DuplicantController.WorkerState.Idle;
        emergencyRestCoroutine = null;
        RequestImmediateTaskSearch();
    }

    private bool TryStartFoodRoutine(bool allowEmergencySources)
    {
        if (FoodSourceRegistry.Instance != null
            && FoodSourceRegistry.Instance.TryReserveReachableFood(
                controller,
                allowEmergencySources,
                out IFoodSource source,
                out List<Vector2Int> path))
        {
            emergencyFoodCoroutine = StartCoroutine(
                EmergencyFoodRoutine(source, path));
            return true;
        }

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        float retry = settings != null ? settings.foodSearchRetryDelay : 3f;
        nextPreventiveFoodSearchTime = Time.time + retry;

        if (allowEmergencySources)
        {
            GameEvents.TriggerFloatingTextRequested(
                "Sem alimento alcançável",
                transform.position,
                Color.red);
        }

        return false;
    }

    private IEnumerator EmergencyFoodRoutine(
        IFoodSource source,
        List<Vector2Int> path)
    {
        controller.CancelCurrentTaskExecution();
        yield return taskRunner.ExecuteEmergencyEatingRoutine(source, path);

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        searchCooldown = settings != null ? settings.foodSearchRetryDelay : 3f;
        nextPreventiveFoodSearchTime = Time.time + searchCooldown;
        emergencyFoodCoroutine = null;
    }
}
