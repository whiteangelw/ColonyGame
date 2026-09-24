using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class CreatureDomesticationSaveData
{
    public bool hasState;
    public float progress;
    public int directFeedingsReceived;
    public bool hasLastDirectFood;
    public int lastDirectFood;
    public string lastResult;
    public float contentRemaining;
    public float overfedRemaining;
    public float rejectedOfferingRemaining;
    public bool hasReceivedDirectFeeding;
    public bool neglected;
    public bool isDomesticated;

    public bool usesCycleCare;
    public int trackedDay;
    public int feedingsThisCycle;
    public bool dailyLimitReached;
    public bool careTrackingStarted;
    public bool hasCompletedCareCycle;
    public bool lastCompletedCycleCareSatisfied;
    public int lastDirectFeedingDay;

    // Compatibilidade com saves da versão baseada em segundos.
    public float refusalRemaining;
    public float nextNeglectRemaining;
    public float neglectSignalRemaining;
    public bool resetWindowAfterRefusal;
    public List<float> feedingWindowRemaining = new List<float>();
}

public enum DirectFeedingOutcome
{
    Accepted,
    AcceptedOverLimit,
    Refused,
    InvalidFood
}

public enum CreatureRelationshipMood
{
    Wild,
    Content,
    Overfed,
    Refusing,
    Neglected,
    Domesticated,
    RejectedOffering
}

[RequireComponent(typeof(CreatureController))]
public sealed class CreatureDomestication : MonoBehaviour
{
    private CreatureController controller;
    private float contentUntil;
    private float overfedUntil;
    private float neglectSignalUntil;
    private float rejectedOfferingUntil;
    private bool hasReceivedDirectFeeding;
    private bool neglected;
    private int trackedDay;
    private int feedingsThisCycle;
    private bool dailyLimitReached;
    private bool careTrackingStarted;
    private bool hasCompletedCareCycle;
    private bool lastCompletedCycleCareSatisfied = true;
    private int lastDirectFeedingDay;

    public float Progress { get; private set; }
    public bool IsDomesticated { get; private set; }
    public int DirectFeedingsReceived { get; private set; }
    public ResourceType? LastDirectFood { get; private set; }
    public string LastResult { get; private set; } =
        "Nunca alimentada diretamente";
    public bool IsRefusing => dailyLimitReached;
    public bool IsOverfed => Time.time < overfedUntil || IsRefusing;
    public bool IsNeglected => careTrackingStarted && neglected;
    public bool ShouldSignalNeglect => IsNeglected
        && Time.time < neglectSignalUntil;
    public bool IsRejectingOffering => Time.time < rejectedOfferingUntil;
    public float RefusalRemaining => 0f;
    public float NeglectGraceRemaining => -1f;
    public int TrackedDay => trackedDay;
    public int FeedingsThisCycle => feedingsThisCycle;
    public int MinimumFeedingsPerCycle => Profile != null
        ? Profile.minimumFeedingsPerCycle : 0;
    public int HealthyFeedingsPerCycle => Profile != null
        ? Profile.healthyFeedingsPerCycle : 0;
    public int MaximumFeedingsPerCycle => Profile != null
        ? Profile.MaximumAcceptedFeedingsPerCycle : 0;
    public bool LastCompletedCycleCareSatisfied =>
        lastCompletedCycleCareSatisfied;
    public bool HasCompletedCareCycle => hasCompletedCareCycle;
    public int LastDirectFeedingDay => lastDirectFeedingDay;
    public bool IsCurrentCycleCareSatisfied => Profile != null
        && feedingsThisCycle >= Mathf.Max(
            0, Profile.minimumFeedingsPerCycle);

    // O último ciclo mantém o benefício durante o dia seguinte. Caso ele tenha
    // sido negligenciado, cumprir o mínimo atual recupera o benefício no mesmo
    // dia. A sincronização evita estado antigo quando o evento do relógio não
    // estiver disponível durante inicialização ou carregamento.
    public bool IsDailyCareSatisfiedForBenefits
    {
        get
        {
            SynchronizeWithClock();
            return IsDomesticated
                && (!hasCompletedCareCycle
                    || lastCompletedCycleCareSatisfied
                    || IsCurrentCycleCareSatisfied);
        }
    }

    public CreatureRelationshipMood Mood
    {
        get
        {
            if (IsRefusing) return CreatureRelationshipMood.Refusing;
            if (IsRejectingOffering)
                return CreatureRelationshipMood.RejectedOffering;
            if (Time.time < overfedUntil)
                return CreatureRelationshipMood.Overfed;
            if (IsNeglected) return CreatureRelationshipMood.Neglected;
            if (IsDomesticated) return CreatureRelationshipMood.Domesticated;
            if (Time.time < contentUntil)
                return CreatureRelationshipMood.Content;
            return CreatureRelationshipMood.Wild;
        }
    }

    private CreatureTamingProfileSO Profile => controller.Definition != null
        ? controller.Definition.tamingProfile
        : null;

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
    }

    private void OnEnable()
    {
        GameEvents.OnDayStarted += HandleDayStarted;
    }

    private void OnDisable()
    {
        GameEvents.OnDayStarted -= HandleDayStarted;
    }

    public void TickRelationship()
    {
        SynchronizeWithClock();
    }

    public bool CanAcceptDirectFeeding(
        ResourceType foodType,
        out string reason)
    {
        SynchronizeWithClock();
        CreatureTamingProfileSO profile = Profile;
        if (profile == null || !profile.TryGetRule(foodType, out _))
        {
            reason = "Alimento não aceito por esta espécie";
            return false;
        }

        if (dailyLimitReached
            || feedingsThisCycle >= profile.MaximumAcceptedFeedingsPerCycle)
        {
            dailyLimitReached = true;
            reason = "Limite diário atingido; volta a aceitar no próximo ciclo";
            return false;
        }

        reason = "Pode receber alimentação direta";
        return true;
    }

    public bool CanReceiveOffering(out string reason)
    {
        SynchronizeWithClock();
        if (Profile == null)
        {
            reason = "Esta espécie não possui regras de alimentação direta";
            return false;
        }

        if (dailyLimitReached)
        {
            reason = "A criatura não aceita mais alimento neste ciclo";
            return false;
        }

        reason = "A oferta pode ser levada até a criatura";
        return true;
    }

    public DirectFeedingOutcome RegisterDirectFeeding(ResourceType foodType)
    {
        SynchronizeWithClock();
        CreatureTamingProfileSO profile = Profile;
        if (profile == null
            || !profile.TryGetRule(foodType, out DirectFeedingRule rule))
        {
            rejectedOfferingUntil = Time.time + (profile != null
                ? Mathf.Max(0.1f, profile.rejectedOfferingBehaviourDuration)
                : 4f);
            LastResult = "Recusou a oferta de " + foodType;
            return DirectFeedingOutcome.InvalidFood;
        }

        if (!CanAcceptDirectFeeding(foodType, out string reason))
        {
            LastResult = reason;
            return DirectFeedingOutcome.Refused;
        }

        int previousCount = feedingsThisCycle;
        feedingsThisCycle++;
        lastDirectFeedingDay = trackedDay;
        DirectFeedingsReceived++;
        LastDirectFood = foodType;
        hasReceivedDirectFeeding = true;
        careTrackingStarted = true;
        neglected = false;
        neglectSignalUntil = 0f;

        bool overHealthyLimit = previousCount
            >= Mathf.Max(1, profile.healthyFeedingsPerCycle);
        DirectFeedingOutcome outcome;
        if (overHealthyLimit)
        {
            float penalty = Mathf.Max(0f,
                profile.overfeedingProgressPenalty);
            Progress = Mathf.Max(0f, Progress - penalty);
            contentUntil = 0f;
            overfedUntil = Time.time + Mathf.Max(
                0.1f, profile.overfedBehaviourDuration);
            LastResult = "Excesso diário de alimento: -"
                + penalty.ToString("0.#") + " de progresso";
            outcome = DirectFeedingOutcome.AcceptedOverLimit;
        }
        else
        {
            Progress = Mathf.Clamp(
                Progress + rule.domesticationProgress, 0f, 100f);
            contentUntil = Time.time + Mathf.Max(
                0.1f, profile.contentBehaviourDuration);
            LastResult = "+" + rule.domesticationProgress.ToString("0.#")
                + " de progresso com " + foodType;
            outcome = DirectFeedingOutcome.Accepted;
        }

        if (feedingsThisCycle >= profile.MaximumAcceptedFeedingsPerCycle)
            dailyLimitReached = true;

        TryCompleteDomestication();
        return outcome;
    }

    public int GetRecentFeedingCount()
    {
        SynchronizeWithClock();
        return feedingsThisCycle;
    }

    public float GetMovementSpeedMultiplier()
    {
        if (IsRefusing || Time.time < overfedUntil) return 0.55f;
        if (IsRejectingOffering) return 0.7f;
        if (Mood == CreatureRelationshipMood.Content) return 1.3f;
        if (ShouldSignalNeglect) return 0.85f;
        return 1f;
    }

    public float GetDecisionIntervalMultiplier()
    {
        if (IsRefusing || Time.time < overfedUntil) return 1.75f;
        if (IsRejectingOffering) return 1.4f;
        if (Mood == CreatureRelationshipMood.Content) return 0.75f;
        if (ShouldSignalNeglect) return 1.25f;
        return 1f;
    }

    public CreatureDomesticationSaveData CaptureState()
    {
        SynchronizeWithClock();
        return new CreatureDomesticationSaveData
        {
            hasState = true,
            progress = Progress,
            directFeedingsReceived = DirectFeedingsReceived,
            hasLastDirectFood = LastDirectFood.HasValue,
            lastDirectFood = LastDirectFood.HasValue
                ? (int)LastDirectFood.Value : 0,
            lastResult = LastResult,
            contentRemaining = Mathf.Max(0f, contentUntil - Time.time),
            overfedRemaining = Mathf.Max(0f, overfedUntil - Time.time),
            rejectedOfferingRemaining = Mathf.Max(
                0f, rejectedOfferingUntil - Time.time),
            hasReceivedDirectFeeding = hasReceivedDirectFeeding,
            neglected = neglected,
            isDomesticated = IsDomesticated,
            usesCycleCare = true,
            trackedDay = trackedDay,
            feedingsThisCycle = feedingsThisCycle,
            dailyLimitReached = dailyLimitReached,
            careTrackingStarted = careTrackingStarted,
            hasCompletedCareCycle = hasCompletedCareCycle,
            lastCompletedCycleCareSatisfied =
                lastCompletedCycleCareSatisfied,
            lastDirectFeedingDay = lastDirectFeedingDay,
            neglectSignalRemaining = Mathf.Max(
                0f, neglectSignalUntil - Time.time)
        };
    }

    public void RestoreState(CreatureDomesticationSaveData data)
    {
        if (data == null || !data.hasState) return;

        Progress = Mathf.Clamp(data.progress, 0f, 100f);
        DirectFeedingsReceived = Mathf.Max(
            0, data.directFeedingsReceived);
        LastDirectFood = data.hasLastDirectFood
            ? (ResourceType?)data.lastDirectFood
            : null;
        LastResult = string.IsNullOrWhiteSpace(data.lastResult)
            ? "Estado restaurado"
            : data.lastResult;
        contentUntil = Time.time + Mathf.Max(0f, data.contentRemaining);
        overfedUntil = Time.time + Mathf.Max(0f, data.overfedRemaining);
        neglectSignalUntil = Time.time + Mathf.Max(
            0f, data.neglectSignalRemaining);
        rejectedOfferingUntil = Time.time + Mathf.Max(
            0f, data.rejectedOfferingRemaining);
        hasReceivedDirectFeeding = data.hasReceivedDirectFeeding;
        neglected = data.neglected;
        IsDomesticated = data.isDomesticated || Progress >= 100f;
        lastDirectFeedingDay = Mathf.Max(0, data.lastDirectFeedingDay);

        if (data.usesCycleCare)
        {
            trackedDay = Mathf.Max(1, data.trackedDay);
            feedingsThisCycle = Mathf.Max(0, data.feedingsThisCycle);
            dailyLimitReached = data.dailyLimitReached;
            careTrackingStarted = data.careTrackingStarted;
            hasCompletedCareCycle = data.hasCompletedCareCycle;
            lastCompletedCycleCareSatisfied =
                data.lastCompletedCycleCareSatisfied;
        }
        else
        {
            // Migrar um save antigo não gera penalidade retroativa.
            trackedDay = GetCurrentDay();
            feedingsThisCycle = 0;
            dailyLimitReached = false;
            careTrackingStarted = hasReceivedDirectFeeding;
            hasCompletedCareCycle = false;
            lastCompletedCycleCareSatisfied = true;
        }

        SynchronizeWithClock();
    }

    private void HandleDayStarted(int newDay)
    {
        SynchronizeToDay(newDay);
    }

    private void SynchronizeWithClock()
    {
        SynchronizeToDay(GetCurrentDay());
    }

    private void SynchronizeToDay(int currentDay)
    {
        currentDay = Mathf.Max(1, currentDay);
        if (trackedDay <= 0)
        {
            trackedDay = currentDay;
            return;
        }

        while (trackedDay < currentDay)
        {
            EvaluateCompletedCycle();
            trackedDay++;
            feedingsThisCycle = 0;
            dailyLimitReached = false;
            contentUntil = 0f;
        }
    }

    private void EvaluateCompletedCycle()
    {
        if (!careTrackingStarted || Profile == null) return;

        int missing = Mathf.Max(
            0, Profile.minimumFeedingsPerCycle - feedingsThisCycle);
        hasCompletedCareCycle = true;
        lastCompletedCycleCareSatisfied = missing == 0;

        if (missing == 0)
        {
            neglected = false;
            LastResult = "Cuidado diário cumprido no ciclo " + trackedDay;
            return;
        }

        float loss = missing * Mathf.Max(
            0f, Profile.neglectPenaltyPerMissingFeeding);
        Progress = Mathf.Max(0f, Progress - loss);
        neglected = true;
        neglectSignalUntil = Time.time + Mathf.Max(
            0.1f, Profile.neglectSignalDuration);
        LastResult = "Negligência no ciclo " + trackedDay + ": -"
            + loss.ToString("0.#") + " de progresso";
    }

    private int GetCurrentDay()
    {
        return DayNightCycleManager.Instance != null
            ? Mathf.Max(1, DayNightCycleManager.Instance.CurrentDay)
            : 1;
    }

    private void TryCompleteDomestication()
    {
        if (IsDomesticated || Progress < 100f) return;

        IsDomesticated = true;
        LastResult = "A criatura passou a confiar na colônia";
        GameEvents.TriggerCreatureDomesticated(controller);
        GameEvents.TriggerFloatingTextRequested(
            "Novo vínculo com a colônia",
            transform.position,
            new Color(0.55f, 1f, 0.7f));
    }
}
