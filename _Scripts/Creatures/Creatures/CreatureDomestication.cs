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
    public float refusalRemaining;
    public float nextNeglectRemaining;
    public float neglectSignalRemaining;
    public bool resetWindowAfterRefusal;
    public bool hasReceivedDirectFeeding;
    public bool neglected;
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
    Domesticated
}

[RequireComponent(typeof(CreatureController))]
public sealed class CreatureDomestication : MonoBehaviour
{
    private readonly Queue<float> recentFeedingTimes = new Queue<float>();
    private CreatureController controller;
    private float contentUntil;
    private float overfedUntil;
    private float refusalUntil;
    private float nextNeglectPenaltyAt;
    private float neglectSignalUntil;
    private bool resetWindowAfterRefusal;
    private bool hasReceivedDirectFeeding;
    private bool neglected;

    public float Progress { get; private set; }
    public int DirectFeedingsReceived { get; private set; }
    public ResourceType? LastDirectFood { get; private set; }
    public string LastResult { get; private set; } = "Nunca alimentada diretamente";
    public bool IsRefusing => Time.time < refusalUntil;
    public bool IsOverfed => Time.time < overfedUntil || IsRefusing;
    public bool IsNeglected => hasReceivedDirectFeeding && neglected;
    public bool ShouldSignalNeglect => IsNeglected
        && Time.time < neglectSignalUntil;
    public float RefusalRemaining => Mathf.Max(0f, refusalUntil - Time.time);
    public float NeglectGraceRemaining => !hasReceivedDirectFeeding
        ? -1f
        : Mathf.Max(0f, nextNeglectPenaltyAt - Time.time);

    public CreatureRelationshipMood Mood
    {
        get
        {
            if (IsRefusing) return CreatureRelationshipMood.Refusing;
            if (Time.time < overfedUntil) return CreatureRelationshipMood.Overfed;
            if (IsNeglected) return CreatureRelationshipMood.Neglected;
            if (Progress >= 100f) return CreatureRelationshipMood.Domesticated;
            if (Time.time < contentUntil) return CreatureRelationshipMood.Content;
            return CreatureRelationshipMood.Wild;
        }
    }

    private CreatureTamingProfileSO Profile => controller.Definition != null
        ? controller.Definition.tamingProfile
        : null;

    private void Awake() => controller = GetComponent<CreatureController>();

    public void TickRelationship()
    {
        CreatureTamingProfileSO profile = Profile;
        if (profile == null) return;

        if (resetWindowAfterRefusal && !IsRefusing)
        {
            recentFeedingTimes.Clear();
            resetWindowAfterRefusal = false;
        }
        else
        {
            PruneFeedingWindow(profile);
        }

        if (!hasReceivedDirectFeeding || IsRefusing
            || Time.time < nextNeglectPenaltyAt)
        {
            return;
        }

        neglected = true;
        neglectSignalUntil = Time.time + Mathf.Max(0.1f,
            profile.neglectSignalDuration);
        float interval = Mathf.Max(1f, profile.neglectPenaltyInterval);
        int elapsedIntervals = 1 + Mathf.FloorToInt(
            (Time.time - nextNeglectPenaltyAt) / interval);
        float loss = elapsedIntervals * Mathf.Max(0f, profile.neglectProgressPenalty);
        Progress = Mathf.Max(0f, Progress - loss);
        nextNeglectPenaltyAt += elapsedIntervals * interval;
        LastResult = "Negligência: -" + loss.ToString("0.#") + " de progresso";
    }

    public bool CanAcceptDirectFeeding(ResourceType foodType, out string reason)
    {
        TickRelationship();
        CreatureTamingProfileSO profile = Profile;
        if (profile == null || !profile.TryGetRule(foodType, out _))
        {
            reason = "Alimento não aceito por esta espécie";
            return false;
        }

        if (IsRefusing)
        {
            reason = "Recusando por " + RefusalRemaining.ToString("0.0") + "s";
            return false;
        }

        PruneFeedingWindow(profile);
        if (recentFeedingTimes.Count >= profile.MaximumAcceptedFeedingsPerWindow)
        {
            BeginRefusal(profile);
            reason = "Limite excedido; iniciou período de recusa";
            return false;
        }

        reason = "Pode receber alimentação direta";
        return true;
    }

    public DirectFeedingOutcome RegisterDirectFeeding(ResourceType foodType)
    {
        CreatureTamingProfileSO profile = Profile;
        if (profile == null || !profile.TryGetRule(foodType, out DirectFeedingRule rule))
        {
            LastResult = foodType + " não é aceito para domesticação";
            return DirectFeedingOutcome.InvalidFood;
        }

        if (!CanAcceptDirectFeeding(foodType, out string reason))
        {
            LastResult = reason;
            return DirectFeedingOutcome.Refused;
        }

        int previousCount = recentFeedingTimes.Count;
        recentFeedingTimes.Enqueue(Time.time);
        DirectFeedingsReceived++;
        LastDirectFood = foodType;
        hasReceivedDirectFeeding = true;
        neglected = false;
        neglectSignalUntil = 0f;
        nextNeglectPenaltyAt = Time.time + Mathf.Max(1f, profile.neglectGraceDuration);

        bool overHealthyLimit = previousCount
            >= Mathf.Max(1, profile.healthyFeedingsPerWindow);
        DirectFeedingOutcome outcome;
        if (overHealthyLimit)
        {
            float penalty = Mathf.Max(0f, profile.overfeedingProgressPenalty);
            Progress = Mathf.Max(0f, Progress - penalty);
            contentUntil = 0f;
            overfedUntil = Time.time + Mathf.Max(0.1f,
                profile.overfedBehaviourDuration);
            LastResult = "Excesso de alimento: -"
                + penalty.ToString("0.#") + " de progresso";
            outcome = DirectFeedingOutcome.AcceptedOverLimit;
        }
        else
        {
            Progress = Mathf.Clamp(Progress + rule.domesticationProgress, 0f, 100f);
            contentUntil = Time.time + Mathf.Max(0.1f,
                profile.contentBehaviourDuration);
            LastResult = "+" + rule.domesticationProgress.ToString("0.#")
                + " de progresso com " + foodType;
            outcome = DirectFeedingOutcome.Accepted;
        }

        if (recentFeedingTimes.Count >= profile.MaximumAcceptedFeedingsPerWindow)
            BeginRefusal(profile);

        return outcome;
    }

    public int GetRecentFeedingCount()
    {
        if (Profile != null) PruneFeedingWindow(Profile);
        return recentFeedingTimes.Count;
    }

    public float GetMovementSpeedMultiplier()
    {
        if (IsRefusing || Time.time < overfedUntil) return 0.55f;
        if (Mood == CreatureRelationshipMood.Content) return 1.3f;
        if (ShouldSignalNeglect) return 0.85f;
        return 1f;
    }

    public float GetDecisionIntervalMultiplier()
    {
        if (IsRefusing || Time.time < overfedUntil) return 1.75f;
        if (Mood == CreatureRelationshipMood.Content) return 0.75f;
        if (ShouldSignalNeglect) return 1.25f;
        return 1f;
    }

    public CreatureDomesticationSaveData CaptureState()
    {
        TickRelationship();
        CreatureDomesticationSaveData data = new CreatureDomesticationSaveData
        {
            hasState = true,
            progress = Progress,
            directFeedingsReceived = DirectFeedingsReceived,
            hasLastDirectFood = LastDirectFood.HasValue,
            lastDirectFood = LastDirectFood.HasValue ? (int)LastDirectFood.Value : 0,
            lastResult = LastResult,
            contentRemaining = Mathf.Max(0f, contentUntil - Time.time),
            overfedRemaining = Mathf.Max(0f, overfedUntil - Time.time),
            refusalRemaining = Mathf.Max(0f, refusalUntil - Time.time),
            nextNeglectRemaining = hasReceivedDirectFeeding
                ? Mathf.Max(0f, nextNeglectPenaltyAt - Time.time)
                : 0f,
            neglectSignalRemaining = Mathf.Max(0f, neglectSignalUntil - Time.time),
            resetWindowAfterRefusal = resetWindowAfterRefusal,
            hasReceivedDirectFeeding = hasReceivedDirectFeeding,
            neglected = neglected
        };

        CreatureTamingProfileSO profile = Profile;
        if (profile != null)
        {
            float window = Mathf.Max(1f, profile.feedingWindowDuration);
            foreach (float feedingTime in recentFeedingTimes)
            {
                float remaining = window - (Time.time - feedingTime);
                if (remaining > 0f) data.feedingWindowRemaining.Add(remaining);
            }
        }

        return data;
    }

    public void RestoreState(CreatureDomesticationSaveData data)
    {
        recentFeedingTimes.Clear();
        if (data == null || !data.hasState) return;

        Progress = Mathf.Clamp(data.progress, 0f, 100f);
        DirectFeedingsReceived = Mathf.Max(0, data.directFeedingsReceived);
        LastDirectFood = data.hasLastDirectFood
            ? (ResourceType?)data.lastDirectFood
            : null;
        LastResult = string.IsNullOrWhiteSpace(data.lastResult)
            ? "Estado restaurado"
            : data.lastResult;
        contentUntil = Time.time + Mathf.Max(0f, data.contentRemaining);
        overfedUntil = Time.time + Mathf.Max(0f, data.overfedRemaining);
        refusalUntil = Time.time + Mathf.Max(0f, data.refusalRemaining);
        nextNeglectPenaltyAt = Time.time + Mathf.Max(0f,
            data.nextNeglectRemaining);
        neglectSignalUntil = Time.time + Mathf.Max(0f,
            data.neglectSignalRemaining);
        resetWindowAfterRefusal = data.resetWindowAfterRefusal;
        hasReceivedDirectFeeding = data.hasReceivedDirectFeeding;
        neglected = data.neglected;

        CreatureTamingProfileSO profile = Profile;
        if (profile == null || data.feedingWindowRemaining == null) return;

        float window = Mathf.Max(1f, profile.feedingWindowDuration);
        for (int i = 0; i < data.feedingWindowRemaining.Count; i++)
        {
            float remaining = Mathf.Clamp(
                data.feedingWindowRemaining[i], 0f, window);
            if (remaining <= 0f) continue;
            recentFeedingTimes.Enqueue(Time.time - (window - remaining));
        }
    }

    private void BeginRefusal(CreatureTamingProfileSO profile)
    {
        contentUntil = 0f;
        refusalUntil = Mathf.Max(refusalUntil,
            Time.time + Mathf.Max(0.1f, profile.refusalDuration));
        overfedUntil = Mathf.Max(overfedUntil, refusalUntil);
        resetWindowAfterRefusal = true;
    }

    private void PruneFeedingWindow(CreatureTamingProfileSO profile)
    {
        float oldestAllowed = Time.time - Mathf.Max(1f,
            profile.feedingWindowDuration);
        while (recentFeedingTimes.Count > 0
            && recentFeedingTimes.Peek() < oldestAllowed)
        {
            recentFeedingTimes.Dequeue();
        }
    }
}
