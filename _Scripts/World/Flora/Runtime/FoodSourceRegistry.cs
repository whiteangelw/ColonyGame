using System.Collections.Generic;
using UnityEngine;

public class FoodSourceRegistry : MonoBehaviour
{
    private sealed class EvaluatedCandidate
    {
        public IFoodSource source;
        public FoodOption option;
        public List<Vector2Int> path;
        public int requestedPortions;
        public float targetHunger;
        public float travelSeconds;
        public float hungerOnArrival;
        public float score;
    }

    public static FoodSourceRegistry Instance { get; private set; }

    [SerializeField, Min(1)] private int fallbackMaximumPathChecks = 8;

    private readonly List<IFoodSource> sources = new List<IFoodSource>();
    private readonly List<IFoodSource> nearbySources = new List<IFoodSource>();
    private readonly List<FoodOption> optionBuffer = new List<FoodOption>();
    private readonly List<EvaluatedCandidate> evaluatedCandidates =
        new List<EvaluatedCandidate>();
    private Vector2Int currentSortOrigin;
    private int searchBudgetFrame = -1;
    private int searchesThisFrame;

    public int AvailableSourceCount => sources.Count;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        MonoBehaviour[] existing = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (MonoBehaviour behaviour in existing)
        {
            if (behaviour is IFoodSource source) Register(source);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(IFoodSource source)
    {
        if (IsValid(source) && !sources.Contains(source)) sources.Add(source);
    }

    public void Unregister(IFoodSource source)
    {
        if (source != null) sources.Remove(source);
    }

    public bool TryCreateMealPlan(
        DuplicantController duplicant,
        out MealPlan mealPlan,
        out FoodSearchFailureReason failureReason,
        out string failureDetails)
    {
        mealPlan = null;
        failureReason = FoodSearchFailureReason.NoFoodAvailable;
        failureDetails = "Nenhuma fonte possui alimento disponível.";

        if (duplicant == null || duplicant.Vitals == null) return false;

        LifeCycleSettingsSO settings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        int searchBudget = settings != null
            ? Mathf.Max(1, settings.maximumFoodSearchesPerFrame)
            : 2;
        if (searchBudgetFrame != Time.frameCount)
        {
            searchBudgetFrame = Time.frameCount;
            searchesThisFrame = 0;
        }

        if (searchesThisFrame >= searchBudget)
        {
            failureReason = FoodSearchFailureReason.SearchDeferred;
            failureDetails = "Busca adiada pelo orçamento de desempenho.";
            return false;
        }

        searchesThisFrame++;
        float hungerPercent = duplicant.Vitals.HungerPercent;
        bool isEmergency = hungerPercent <= 0f;
        bool isCritical = isEmergency || duplicant.Vitals.IsStarving;
        bool allowEmergencySources = settings == null
            ? isEmergency
            : hungerPercent <= settings.rawFoodAllowedBelow;
        float targetPercent = GetMealTarget(settings, isCritical, isEmergency);
        float targetHunger = duplicant.Vitals.MaximumHunger * targetPercent;
        float hungerNeeded = Mathf.Max(
            0.1f,
            targetHunger - duplicant.Vitals.CurrentHunger);
        int searchRadius = GetSearchRadius(settings, isCritical, isEmergency);
        int maximumChecks = settings != null
            ? Mathf.Max(1, settings.maximumFoodPathChecks)
            : fallbackMaximumPathChecks;

        sources.RemoveAll(source => !IsValid(source));
        nearbySources.Clear();
        evaluatedCandidates.Clear();

        bool foundFood = false;
        bool foundSourceInRadius = false;
        bool foundReachableSource = false;
        bool foundUnsafeJourney = false;

        foreach (IFoodSource source in sources)
        {
            if (!source.HasFood) continue;
            foundFood = true;
            if (source.IsEmergencyOnly && !allowEmergencySources) continue;
            if (ManhattanDistance(duplicant.gridPosition, source.GridPosition)
                > searchRadius)
            {
                continue;
            }

            foundSourceInRadius = true;
            nearbySources.Add(source);
        }

        currentSortOrigin = duplicant.gridPosition;
        nearbySources.Sort(CompareApproximateDistance);

        int checks = Mathf.Min(maximumChecks, nearbySources.Count);
        for (int i = 0; i < checks; i++)
        {
            IFoodSource source = nearbySources[i];
            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToInteractionPosition(
                    duplicant.gridPosition,
                    source.GridPosition,
                    true,
                    duplicant.capabilityProfile);

            if (path == null) continue;
            foundReachableSource = true;

            float travelSeconds = EstimateTravelSeconds(
                duplicant,
                path.Count,
                settings);
            float hungerOnArrival = duplicant.Vitals.EstimateHungerAfterSeconds(
                travelSeconds,
                settings);

            if (!IsJourneySafe(
                    duplicant.Vitals,
                    hungerOnArrival,
                    settings,
                    isCritical,
                    isEmergency))
            {
                foundUnsafeJourney = true;
                continue;
            }

            optionBuffer.Clear();
            source.CollectFoodOptions(optionBuffer);
            foreach (FoodOption option in optionBuffer)
            {
                if (!option.IsValid) continue;
                if (!option.allowPreventiveConsumption
                    && !allowEmergencySources)
                {
                    continue;
                }

                int portionsNeeded = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        hungerNeeded / option.hungerRestoredPerPortion));
                int requestedPortions = Mathf.Min(
                    portionsNeeded,
                    option.availablePortions);
                if (requestedPortions <= 0) continue;

                float expected = requestedPortions
                    * option.hungerRestoredPerPortion;
                float waste = Mathf.Max(0f, expected - hungerNeeded);

                evaluatedCandidates.Add(new EvaluatedCandidate
                {
                    source = source,
                    option = option,
                    path = path,
                    requestedPortions = requestedPortions,
                    targetHunger = targetHunger,
                    travelSeconds = travelSeconds,
                    hungerOnArrival = hungerOnArrival,
                    score = CalculateScore(
                        source,
                        option,
                        path.Count,
                        requestedPortions,
                        waste,
                        settings)
                });
            }
        }

        evaluatedCandidates.Sort((a, b) => a.score.CompareTo(b.score));
        foreach (EvaluatedCandidate candidate in evaluatedCandidates)
        {
            if (!IsValid(candidate.source)) continue;
            if (!candidate.source.TryReserveMeal(
                    duplicant,
                    candidate.option.resourceType,
                    candidate.requestedPortions,
                    out int reservedPortions))
            {
                continue;
            }

            mealPlan = new MealPlan(
                candidate.source,
                candidate.option.resourceType,
                reservedPortions,
                candidate.targetHunger,
                reservedPortions
                    * candidate.option.hungerRestoredPerPortion,
                candidate.travelSeconds,
                candidate.hungerOnArrival,
                candidate.option.isRawFood,
                candidate.path.Count > 0
                    ? candidate.path[candidate.path.Count - 1]
                    : duplicant.gridPosition,
                candidate.path);
            failureReason = FoodSearchFailureReason.None;
            failureDetails = string.Empty;
            return true;
        }

        if (evaluatedCandidates.Count > 0)
        {
            failureReason = FoodSearchFailureReason.FoodReservationFailed;
            failureDetails = "As opções seguras foram reservadas por outro duplicant.";
        }
        else if (foundUnsafeJourney)
        {
            failureReason = FoodSearchFailureReason.FoodJourneyUnsafe;
            failureDetails = "A viagem consumiria uma margem insegura de fome.";
        }
        else if (foundReachableSource)
        {
            failureReason = FoodSearchFailureReason.NoFoodAvailable;
            failureDetails = "Nenhum alimento permitido atende à necessidade atual.";
        }
        else if (foundSourceInRadius || foundFood)
        {
            failureReason = FoodSearchFailureReason.FoodUnreachable;
            failureDetails = foundSourceInRadius
                ? "Existe alimento próximo, mas nenhuma posição de interação alcançável."
                : "Existe alimento, mas está fora do raio seguro de busca.";
        }

        return false;
    }

    private static float GetMealTarget(
        LifeCycleSettingsSO settings,
        bool isCritical,
        bool isEmergency)
    {
        if (settings == null)
        {
            return isEmergency ? 1f : isCritical ? 0.95f : 0.75f;
        }

        if (isEmergency) return settings.emergencyMealTarget;
        return isCritical
            ? settings.criticalMealTarget
            : settings.preventiveMealTarget;
    }

    private static int GetSearchRadius(
        LifeCycleSettingsSO settings,
        bool isCritical,
        bool isEmergency)
    {
        if (settings == null) return isEmergency ? 40 : isCritical ? 80 : 120;
        if (isEmergency) return settings.emergencyFoodSearchRadius;
        return isCritical
            ? settings.criticalFoodSearchRadius
            : settings.preventiveFoodSearchRadius;
    }

    private static float EstimateTravelSeconds(
        DuplicantController duplicant,
        int pathLength,
        LifeCycleSettingsSO settings)
    {
        float cellSize = GridManager.Instance != null
            ? GridManager.Instance.cellSize
            : 1f;
        float speed = Mathf.Max(0.1f, duplicant.baseMoveSpeed);
        float safetyMultiplier = settings != null
            ? settings.travelTimeSafetyMultiplier
            : 1.35f;
        return pathLength * cellSize / speed * safetyMultiplier;
    }

    private static bool IsJourneySafe(
        DuplicantVitals vitals,
        float hungerOnArrival,
        LifeCycleSettingsSO settings,
        bool isCritical,
        bool isEmergency)
    {
        if (isEmergency) return true;
        float minimumPercent = isCritical
            ? 0f
            : (settings != null ? settings.safeArrivalHungerMargin : 0.10f);
        return hungerOnArrival >= vitals.MaximumHunger * minimumPercent;
    }

    private static float CalculateScore(
        IFoodSource source,
        FoodOption option,
        int pathLength,
        int portions,
        float waste,
        LifeCycleSettingsSO settings)
    {
        float score = pathLength;
        score += portions * 2f;
        score += waste * 0.05f;
        score += option.isRawFood ? 8f : 0f;
        score -= option.quality * 0.5f;
        switch (source.SourceKind)
        {
            case FoodSourceKind.Storage:
                score += settings != null
                    ? settings.storageFoodScoreAdjustment
                    : -10f;
                break;
            case FoodSourceKind.GroundItem:
                score += settings != null
                    ? settings.groundFoodScoreAdjustment
                    : 0f;
                break;
            case FoodSourceKind.Flora:
                score += settings != null
                    ? settings.floraFoodScoreAdjustment
                    : 12f;
                break;
        }
        return score;
    }

    private int CompareApproximateDistance(IFoodSource a, IFoodSource b)
    {
        return ManhattanDistance(currentSortOrigin, a.GridPosition).CompareTo(
            ManhattanDistance(currentSortOrigin, b.GridPosition));
    }

    private static bool IsValid(IFoodSource source)
    {
        return source != null
            && (!(source is Object unityObject) || unityObject != null);
    }

    private static int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
