using System.Collections.Generic;
using UnityEngine;

public enum CreatureGardeningActivityState
{
    Idle,
    Travelling,
    Tending,
    Cooldown
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CreatureController), typeof(CreatureMovement))]
public sealed class CreatureTerritorialGardener : MonoBehaviour
{
    private readonly List<PlanterStructureBehaviour> planterSnapshot =
        new List<PlanterStructureBehaviour>();
    private readonly Dictionary<PlanterStructureBehaviour, float>
        failedTargets = new Dictionary<PlanterStructureBehaviour, float>();

    private CreatureController controller;
    private CreatureMovement movement;
    private PlanterStructureBehaviour targetPlanter;
    private PlanterStructureBehaviour lastAffectedPlanter;
    private Vector2Int interactionPosition;
    private float tendingEndsAt;
    private float nextRoundAt;
    private int visitsThisRound;

    public bool HasAbility => Profile != null;
    public bool IsEligible => EvaluateEligibility(out _);
    public float BonusPercent => Profile != null
        ? Mathf.Max(0f, Profile.growthSpeedBonusPercent) : 0f;
    public float InteractionRadius => Profile != null
        ? Mathf.Max(0f, Profile.interactionRadius) : 0f;
    public CreatureGardeningActivityState ActivityState { get; private set; }
        = CreatureGardeningActivityState.Idle;
    public string TargetPlanterPosition => targetPlanter != null
        ? targetPlanter.GridPosition.ToString() : "nenhuma";
    public Vector2Int? InteractionPosition => targetPlanter != null
        ? interactionPosition : (Vector2Int?)null;
    public int VisitsThisRound => visitsThisRound;
    public int ReachableCandidateCount { get; private set; }
    public int UnreachableCandidateCount { get; private set; }
    public float TendingRemaining => ActivityState
            == CreatureGardeningActivityState.Tending
        ? Mathf.Max(0f, tendingEndsAt - Time.time) : 0f;
    public float RoundCooldownRemaining => Mathf.Max(0f, nextRoundAt - Time.time);
    public string LastAffectedPlanter { get; private set; } = "nenhuma";
    public float LastAppliedBonusPercent { get; private set; }
    public float LastAppliedDuration { get; private set; }
    public bool LastEffectActive => lastAffectedPlanter != null
        && lastAffectedPlanter.HasActiveExternalGrowthEffect;
    public float LastEffectRemaining => lastAffectedPlanter != null
        ? lastAffectedPlanter.ExternalGrowthEffectRemaining
        : 0f;
    public string LastResult { get; private set; } = "Aguardando avaliação";

    private CreatureGardeningProfileSO Profile =>
        controller != null && controller.Definition != null
            ? controller.Definition.gardeningProfile : null;

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
        movement = GetComponent<CreatureMovement>();
    }

    private void OnDisable()
    {
        CancelCurrentVisit(false, "Componente desativado");
        CreatureGardeningRegistry.ReleaseAll(this);
    }

    // Retorna true apenas enquanto a jardinagem ocupa a decisão atual. Sem
    // trabalho válido, o Brain continua normalmente até o patrulhamento.
    public bool HandleDecision()
    {
        if (!EvaluateEligibility(out string failure))
        {
            if (ActivityState == CreatureGardeningActivityState.Travelling
                || ActivityState == CreatureGardeningActivityState.Tending)
                CancelCurrentVisit(true, failure);
            LastResult = failure;
            return false;
        }

        controller.RefreshGridPosition();
        CleanupFailedTargets();

        if (ActivityState == CreatureGardeningActivityState.Travelling)
            return HandleTravelling();
        if (ActivityState == CreatureGardeningActivityState.Tending)
            return HandleTending();

        if (Time.time < nextRoundAt)
        {
            ActivityState = CreatureGardeningActivityState.Cooldown;
            return false;
        }

        ActivityState = CreatureGardeningActivityState.Idle;
        visitsThisRound = 0;
        return TryStartNextVisit();
    }

    public void CancelCurrentVisit(bool cancelMovement, string reason)
    {
        if (cancelMovement) movement?.CancelMovement();
        CreatureGardeningRegistry.Release(targetPlanter, this);
        targetPlanter = null;
        tendingEndsAt = 0f;
        ActivityState = CreatureGardeningActivityState.Idle;
        LastResult = reason;
    }

    private bool HandleTravelling()
    {
        if (!IsTargetStillValid())
        {
            RegisterTargetFailure("Plantadeira deixou de ser válida");
            return false;
        }
        if (movement.IsMoving) return true;

        if (IsAtInteractionPosition() && IsCurrentPositionNavigable())
        {
            BeginTending();
            return true;
        }

        string failure = string.IsNullOrWhiteSpace(movement.LastFailure)
            ? "Movimento interrompido antes da chegada"
            : movement.LastFailure;
        RegisterTargetFailure(failure);
        return false;
    }

    private bool HandleTending()
    {
        if (!IsTargetStillValid()
            || !IsAtInteractionPosition()
            || !IsCurrentPositionNavigable())
        {
            RegisterTargetFailure("Cuidado interrompido após mudança no ambiente");
            return false;
        }

        if (Time.time < tendingEndsAt)
        {
            controller.SetDecision(
                CreatureState.Idle,
                "Cuidando da plantadeira",
                targetPlanter.GridPosition);
            return true;
        }

        PlanterStructureBehaviour completedTarget = targetPlanter;
        bool applied = completedTarget.ApplyTimedExternalGrowthSpeedEffect(
            "creature-gardener",
            BonusPercent,
            Profile.bonusDuration,
            Profile.effectPulseInterval);

        CreatureGardeningRegistry.Release(completedTarget, this);
        targetPlanter = null;
        tendingEndsAt = 0f;

        if (!applied)
        {
            failedTargets[completedTarget] = Time.time
                + Profile.failedTargetCooldown;
            LastResult = "Plantadeira recusou o efeito temporário";
            ActivityState = CreatureGardeningActivityState.Idle;
            return false;
        }

        visitsThisRound++;
        LastAffectedPlanter = completedTarget.GridPosition.ToString();
        lastAffectedPlanter = completedTarget;
        LastAppliedBonusPercent = BonusPercent;
        LastAppliedDuration = Profile.bonusDuration;
        LastResult = "Bônus aplicado; procurando próxima plantadeira";
        ActivityState = CreatureGardeningActivityState.Idle;

        if (visitsThisRound >= Profile.maximumVisitsPerRound)
        {
            BeginRoundCooldown("Rodada de jardinagem concluída");
            return false;
        }

        return TryStartNextVisit();
    }

    private bool TryStartNextVisit()
    {
        if (!TryFindBestReachableTarget(
            out PlanterStructureBehaviour planter,
            out Vector2Int approach))
        {
            BeginRoundCooldown(UnreachableCandidateCount > 0
                ? "Plantadeiras encontradas, mas sem caminho alcançável"
                : "Nenhuma plantadeira crescente sem bônus");
            return false;
        }

        float reservationDuration = Mathf.Max(
            60f,
            Profile.tendingDuration + Profile.failedTargetCooldown + 10f);
        if (!CreatureGardeningRegistry.TryReserve(
            planter, this, reservationDuration))
        {
            LastResult = "Plantadeira escolhida por outra criatura";
            controller.Brain?.ForceDecision();
            return false;
        }

        targetPlanter = planter;
        interactionPosition = approach;

        if (controller.GridPosition == interactionPosition)
        {
            BeginTending();
            return true;
        }

        controller.SetDecision(
            CreatureState.Investigate,
            "Indo cuidar da plantadeira",
            interactionPosition);
        if (!movement.TryMoveTo(interactionPosition))
        {
            RegisterTargetFailure(movement.LastFailure);
            return false;
        }

        ActivityState = CreatureGardeningActivityState.Travelling;
        LastResult = "Caminhando até " + interactionPosition;
        return true;
    }

    private bool TryFindBestReachableTarget(
        out PlanterStructureBehaviour selected,
        out Vector2Int selectedApproach)
    {
        selected = null;
        selectedApproach = controller.GridPosition;
        ReachableCandidateCount = 0;
        UnreachableCandidateCount = 0;
        int bestPathLength = int.MaxValue;

        PlanterGrowthRegistry.FillSnapshot(planterSnapshot);
        for (int i = 0; i < planterSnapshot.Count; i++)
        {
            PlanterStructureBehaviour planter = planterSnapshot[i];
            if (!IsCandidate(planter)) continue;

            if (!TryFindApproach(planter, out Vector2Int approach,
                out int pathLength))
            {
                UnreachableCandidateCount++;
                failedTargets[planter] = Time.time
                    + Profile.failedTargetCooldown;
                continue;
            }

            ReachableCandidateCount++;
            if (pathLength >= bestPathLength) continue;
            bestPathLength = pathLength;
            selected = planter;
            selectedApproach = approach;
        }

        return selected != null;
    }

    private bool IsCandidate(PlanterStructureBehaviour planter)
    {
        if (planter == null || !planter.IsGrowing
            || planter.HasActiveExternalGrowthEffect)
            return false;
        if (failedTargets.TryGetValue(planter, out float retryAt)
            && Time.time < retryAt)
            return false;
        if (CreatureGardeningRegistry.IsReservedByOther(planter, this))
            return false;

        return GridDistance(controller.HomePosition, planter.GridPosition)
            <= InteractionRadius;
    }

    private bool TryFindApproach(
        PlanterStructureBehaviour planter,
        out Vector2Int selected,
        out int selectedPathLength)
    {
        selected = controller.GridPosition;
        selectedPathLength = int.MaxValue;
        if (GridManager.Instance == null || NavGraphGenerator.Instance == null
            || PathfindingAStar.Instance == null)
            return false;

        int distance = Mathf.Max(1, Profile.interactionDistance);
        for (int radius = 0; radius <= distance; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int y = radius - Mathf.Abs(x);
                TryApproachCandidate(planter, new Vector2Int(x, y),
                    ref selected, ref selectedPathLength);
                if (y != 0)
                    TryApproachCandidate(planter, new Vector2Int(x, -y),
                        ref selected, ref selectedPathLength);
            }
        }

        return selectedPathLength < int.MaxValue;
    }

    private void TryApproachCandidate(
        PlanterStructureBehaviour planter,
        Vector2Int offset,
        ref Vector2Int selected,
        ref int selectedPathLength)
    {
        Vector2Int candidate = planter.GridPosition + offset;
        if (!GridManager.Instance.IsInsideGrid(candidate.x, candidate.y)
            || !NavGraphGenerator.Instance.IsNavigablePosition(
                candidate.x, candidate.y))
            return;

        List<Vector2Int> path = PathfindingAStar.Instance.FindPath(
            controller.GridPosition,
            candidate,
            controller.Definition.navigationProfile);
        if (path == null || path.Count >= selectedPathLength) return;

        selected = candidate;
        selectedPathLength = path.Count;
    }

    private void BeginTending()
    {
        movement.CancelMovement();
        ActivityState = CreatureGardeningActivityState.Tending;
        tendingEndsAt = Time.time + Mathf.Max(0.1f, Profile.tendingDuration);
        controller.SetDecision(
            CreatureState.Idle,
            "Cuidando da plantadeira",
            targetPlanter.GridPosition);
        LastResult = "Cuidando até concluir a visita";
    }

    private void RegisterTargetFailure(string reason)
    {
        PlanterStructureBehaviour failed = targetPlanter;
        movement.CancelMovement();
        CreatureGardeningRegistry.Release(failed, this);
        if (failed != null)
            failedTargets[failed] = Time.time + Profile.failedTargetCooldown;

        targetPlanter = null;
        tendingEndsAt = 0f;
        ActivityState = CreatureGardeningActivityState.Idle;
        LastResult = "Visita cancelada: " + reason;
        controller.SetDecision(CreatureState.Idle, LastResult);
        controller.Brain?.ForceDecision();
    }

    private void BeginRoundCooldown(string reason)
    {
        ActivityState = CreatureGardeningActivityState.Cooldown;
        nextRoundAt = Time.time + Mathf.Max(
            0.1f, Profile.gardeningRoundCooldown);
        visitsThisRound = 0;
        LastResult = reason;
    }

    private bool IsTargetStillValid()
    {
        return targetPlanter != null
            && targetPlanter.IsGrowing
            && !targetPlanter.HasActiveExternalGrowthEffect;
    }

    private bool IsAtInteractionPosition()
    {
        return controller.GridPosition == interactionPosition
            && targetPlanter != null
            && Manhattan(controller.GridPosition, targetPlanter.GridPosition)
                <= Profile.interactionDistance;
    }

    private bool IsCurrentPositionNavigable()
    {
        return NavGraphGenerator.Instance != null
            && NavGraphGenerator.Instance.IsNavigablePosition(
                controller.GridPosition.x,
                controller.GridPosition.y);
    }

    private bool EvaluateEligibility(out string failure)
    {
        if (controller == null || !controller.IsInitialized)
        {
            failure = "Criatura ainda não inicializada";
            return false;
        }
        if (Profile == null || BonusPercent <= 0f)
        {
            failure = "Espécie sem habilidade configurada";
            return false;
        }
        if (controller.Domestication == null
            || !controller.Domestication.IsDomesticated)
        {
            failure = "Criatura não domesticada";
            return false;
        }
        if (!controller.Domestication.IsDailyCareSatisfiedForBenefits)
        {
            failure = "Cuidado diário não foi cumprido";
            return false;
        }
        if (controller.HabitatLink == null || !controller.HabitatLink.IsBound)
        {
            failure = "Criatura sem habitat vinculado";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void CleanupFailedTargets()
    {
        List<PlanterStructureBehaviour> remove =
            new List<PlanterStructureBehaviour>();
        foreach (KeyValuePair<PlanterStructureBehaviour, float> pair
            in failedTargets)
        {
            if (pair.Key == null || Time.time >= pair.Value)
                remove.Add(pair.Key);
        }

        for (int i = 0; i < remove.Count; i++)
            failedTargets.Remove(remove[i]);
    }

    private static int Manhattan(Vector2Int left, Vector2Int right)
    {
        return Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);
    }

    private static float GridDistance(Vector2Int left, Vector2Int right)
    {
        int deltaX = left.x - right.x;
        int deltaY = left.y - right.y;
        return Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
