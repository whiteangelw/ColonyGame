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

    public MealPlan CurrentMealPlan { get; private set; }
    public FoodSearchFailureReason LastFoodFailureReason { get; private set; }
    public string LastFoodFailureDetails { get; private set; } = "Nenhum";

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
            CurrentMealPlan = null;
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

            bool maySeekFood = controller.currentState
                    == DuplicantController.WorkerState.Idle
                || (vitals != null && vitals.IsStarving);

            if (vitals != null
                && (vitals.IsHungry || vitals.IsStarving)
                && !vitals.IsEmergencyResting
                && emergencyRestCoroutine == null
                && emergencyFoodCoroutine == null
                && maySeekFood
                && Time.time >= nextPreventiveFoodSearchTime)
            {
                if (TryStartFoodRoutine()) continue;
            }

            if (controller.currentState == DuplicantController.WorkerState.Idle && controller.currentTask == null && !movement.ShouldFall())
            {
                if (taskRunner != null && taskRunner.HasCarriedInventory)
                {
                    activeTaskCoroutine = StartCoroutine(
                        StoreInterruptedInventoryRoutine());
                    continue;
                }

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

    private IEnumerator StoreInterruptedInventoryRoutine()
    {
        yield return taskRunner.StoreCarriedInventoryRoutine();
        activeTaskCoroutine = null;
        searchCooldown = 0f;
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
        if (activeTaskCoroutine != null)
        {
            StopCoroutine(activeTaskCoroutine);
            activeTaskCoroutine = null;
        }

        if (emergencyFoodCoroutine != null)
        {
            StopCoroutine(emergencyFoodCoroutine);
            emergencyFoodCoroutine = null;
            taskRunner?.CancelEmergencyFoodState();
            CurrentMealPlan = null;
        }

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
            CurrentMealPlan = null;
        }

        controller.CancelCurrentTaskExecution(
            TaskInterruptionOrigin.EmergencyRest);
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

    private bool TryStartFoodRoutine()
    {
        MealPlan mealPlan;
        FoodSearchFailureReason failureReason =
            FoodSearchFailureReason.NoFoodAvailable;
        string failureDetails = "FoodSourceRegistry não encontrado.";

        if (FoodSourceRegistry.Instance != null
            && FoodSourceRegistry.Instance.TryCreateMealPlan(
                controller,
                out mealPlan,
                out failureReason,
                out failureDetails))
        {
            // Cancela o trabalho antes de registrar a rotina alimentar.
            // Assim, CancelCurrentTaskExecution não interrompe a própria refeição.
            controller.CancelCurrentTaskExecution(
                TaskInterruptionOrigin.FoodNeed);
            CurrentMealPlan = mealPlan;
            LastFoodFailureReason = FoodSearchFailureReason.None;
            LastFoodFailureDetails = "Nenhum";
            emergencyFoodCoroutine = StartCoroutine(
                EatingRoutine(mealPlan));
            return true;
        }

        LastFoodFailureReason = FoodSourceRegistry.Instance != null
            ? failureReason
            : FoodSearchFailureReason.NoFoodAvailable;
        LastFoodFailureDetails = FoodSourceRegistry.Instance != null
            ? failureDetails
            : "FoodSourceRegistry não encontrado.";

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        float retry = failureReason == FoodSearchFailureReason.SearchDeferred
            ? 0.15f
            : (settings != null ? settings.foodSearchRetryDelay : 3f);
        nextPreventiveFoodSearchTime = Time.time + retry;

        if (vitals != null && vitals.CurrentHunger <= 0f)
        {
            GameEvents.TriggerFloatingTextRequested(
                LastFoodFailureReason == FoodSearchFailureReason.FoodJourneyUnsafe
                    ? "Comida distante demais"
                    : "Sem alimento seguro",
                transform.position,
                Color.red);
        }

        return false;
    }

    private IEnumerator EatingRoutine(MealPlan mealPlan)
    {
        yield return taskRunner.ExecuteEatingRoutine(mealPlan);

        if (taskRunner.LastMealConsumedPortions <= 0)
        {
            LastFoodFailureReason = FoodSearchFailureReason.FoodUnreachable;
            LastFoodFailureDetails = taskRunner.LastMealDetails;
        }

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        searchCooldown = settings != null ? settings.foodSearchRetryDelay : 3f;
        nextPreventiveFoodSearchTime = Time.time + searchCooldown;
        emergencyFoodCoroutine = null;
        CurrentMealPlan = null;
    }
}
