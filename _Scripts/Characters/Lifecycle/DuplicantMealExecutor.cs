using System.Collections;
using UnityEngine;

/// <summary>
/// Executa uma refeição já planejada e já reservada.
/// Não procura comida nem decide quando o duplicant deve comer.
/// </summary>
public sealed class DuplicantMealExecutor
{
    private readonly DuplicantController controller;
    private readonly DuplicantPathFollower pathFollower;

    private IFoodSource reservedFoodSource;

    public int LastConsumedPortions { get; private set; }

    public string LastDetails { get; private set; } =
        "Nenhuma refeição executada.";

    public DuplicantMealExecutor(
        DuplicantController controller,
        DuplicantPathFollower pathFollower)
    {
        this.controller = controller;
        this.pathFollower = pathFollower;
    }

    public IEnumerator Execute(MealPlan mealPlan)
    {
        LastConsumedPortions = 0;
        LastDetails = "Plano de refeição inválido.";

        reservedFoodSource = mealPlan != null
            ? mealPlan.Source
            : null;

        if (mealPlan == null
            || !IsFoodSourceValid(reservedFoodSource)
            || reservedFoodSource.GetReservedPortionCount(
                controller) <= 0)
        {
            Cancel();
            yield break;
        }

        yield return pathFollower.FollowInteraction(
            reservedFoodSource.GridPosition,
            mealPlan.Path,
            mealPlan.InteractionPosition);

        if (!pathFollower.Succeeded)
        {
            LastDetails =
                "A posição reservada do baú ficou inalcançável.";

            Cancel();
            controller.currentState =
                DuplicantController.WorkerState.Idle;

            yield break;
        }

        if (!IsFoodSourceValid(reservedFoodSource))
        {
            LastDetails =
                "A fonte de alimento foi destruída ou desativada.";

            Cancel();
            controller.currentState =
                DuplicantController.WorkerState.Idle;

            yield break;
        }

        if (reservedFoodSource.GetReservedPortionCount(
                controller) <= 0)
        {
            LastDetails =
                "A reserva alimentar foi liberada antes do consumo.";

            Cancel();
            controller.currentState =
                DuplicantController.WorkerState.Idle;

            yield break;
        }

        controller.currentState =
            DuplicantController.WorkerState.Eating;

        LifeCycleSettingsSO settings =
            LifeCycleSystem.Instance != null
                ? LifeCycleSystem.Instance.Settings
                : null;

        int consumedPortions = 0;
        float totalRestored = 0f;

        while (controller.Vitals != null
            && controller.Vitals.CurrentHunger
                < mealPlan.HungerTarget
            && IsFoodSourceValid(reservedFoodSource)
            && reservedFoodSource.GetReservedPortionCount(
                controller) > 0)
        {
            yield return new WaitForSeconds(
                settings != null
                    ? settings.foodEatingDurationPerPortion
                    : 1.2f);

            IFoodSource food = reservedFoodSource;

            if (!IsFoodSourceValid(food)
                || !food.TryConsumeReservedPortion(
                    controller,
                    out float hungerRestored,
                    out bool isRawFood))
            {
                LastDetails = IsFoodSourceValid(food)
                    ? "A fonte recusou uma porção que ainda "
                        + "constava como reservada."
                    : "A fonte foi invalidada durante o consumo.";

                break;
            }

            controller.Vitals.RestoreHunger(hungerRestored);

            ApplyRawFoodEffectIfNeeded(
                isRawFood,
                settings);

            totalRestored += hungerRestored;
            consumedPortions++;
        }

        Cancel();

        if (consumedPortions > 0)
        {
            LastConsumedPortions = consumedPortions;
            LastDetails =
                $"Consumiu {consumedPortions} porção(ões).";

            GameEvents.TriggerFloatingTextRequested(
                $"Refeição: {consumedPortions}x "
                    + $"(+{totalRestored:F0})",
                controller.transform.position,
                Color.green);
        }
        else if (LastDetails == "Plano de refeição inválido.")
        {
            LastDetails =
                "Nenhuma porção reservada pôde ser consumida.";
        }

        controller.currentState =
            DuplicantController.WorkerState.Idle;
    }

    public void Cancel()
    {
        if (reservedFoodSource != null)
        {
            reservedFoodSource.ReleaseReservation(controller);
            reservedFoodSource = null;
        }
    }

    private static bool IsFoodSourceValid(IFoodSource source)
    {
        return source != null
            && (!(source is Object unityObject)
                || unityObject != null);
    }

    private void ApplyRawFoodEffectIfNeeded(
        bool isRawFood,
        LifeCycleSettingsSO settings)
    {
        if (!isRawFood
            || settings == null
            || controller.StatusEffects == null
            || Random.value
                > settings.rawFoodDiscomfortChance)
        {
            return;
        }

        controller.StatusEffects.ApplyRawFoodDiscomfort(
            settings.rawFoodDiscomfortDuration,
            settings.rawFoodWorkPenaltyPercent);

        GameEvents.TriggerFloatingTextRequested(
            "Desconforto por alimento cru",
            controller.transform.position,
            Color.yellow);
    }
}