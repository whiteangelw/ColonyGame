using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CreatureDebugPanel : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.F2;
    private bool visible;
    private CreatureController selected;
    private Vector2 scroll;

    private void Update()
    {
        if (Keyboard.current != null
            && Keyboard.current[toggleKey].wasPressedThisFrame)
            visible = !visible;

        if (!visible || Mouse.current == null
            || !Mouse.current.leftButton.wasPressedThisFrame || Camera.main == null) return;

        Vector3 world = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Collider2D hit = Physics2D.OverlapPoint(new Vector2(world.x, world.y));
        CreatureController creature = hit != null
            ? hit.GetComponentInParent<CreatureController>() : null;
        if (creature != null) selected = creature;
    }

    private void OnGUI()
    {
        if (!visible) return;
        GUILayout.BeginArea(new Rect(12, 12, 390, 520), GUI.skin.box);
        GUILayout.Label("CRIATURAS — F2");
        DrawSelectionButtons();
        scroll = GUILayout.BeginScrollView(scroll);

        if (selected == null)
        {
            GUILayout.Label("Clique em uma criatura ou use Próxima.");
        }
        else
        {
            CreatureDefinitionSO definition = selected.Definition;
            GUILayout.Label("Nome: " + (definition != null ? definition.displayName : selected.name));
            GUILayout.Label("Estado: " + selected.State);
            GUILayout.Label("Inicializada: " + selected.IsInitialized);
            GUILayout.Label("Origem: " + selected.SpawnOrigin);
            GUILayout.Label("Bioma: " + (string.IsNullOrEmpty(selected.SpawnBiomeId)
                ? "não informado" : selected.SpawnBiomeId));
            GUILayout.Label("Grid: " + selected.GridPosition);
            GUILayout.Label("Casa: " + selected.HomePosition);
            GUILayout.Label("Casa natural: " + selected.NaturalHomePosition);
            bool homeNavigable = NavGraphGenerator.Instance != null
                && NavGraphGenerator.Instance.IsNavigablePosition(
                    selected.HomePosition.x, selected.HomePosition.y);
            GUILayout.Label("Casa navegável: " + homeNavigable);
            CreatureHabitatLink habitatLink = selected.HabitatLink;
            GUILayout.Space(8);
            GUILayout.Label("HABITAT (INTERNO)");
            GUILayout.Label("Estado: " + (habitatLink != null
                ? habitatLink.State.ToString() : "componente ausente"));
            GUILayout.Label("Habitat: " + (habitatLink != null
                ? habitatLink.HabitatName : "--"));
            GUILayout.Label("Tentativas: " + (habitatLink != null
                ? habitatLink.FailedPathAttempts.ToString() : "--"));
            GUILayout.Label("Último resultado: " + (habitatLink != null
                ? habitatLink.LastResult : "--"));
            GUILayout.Space(8);
            GUILayout.Label("DECISÃO");
            GUILayout.Label("Motivo: " + selected.LastDecisionReason);
            GUILayout.Label("Alvo: " + (selected.CurrentTarget.HasValue
                ? selected.CurrentTarget.Value.ToString() : "nenhum"));
            GUILayout.Label("Próxima: " + Mathf.Max(0f,
                selected.Brain.NextDecisionTime - Time.time).ToString("0.00") + "s");
            GUILayout.Space(8);
            GUILayout.Label("CAMINHO");
            GUILayout.Label("Movendo: " + selected.Movement.IsMoving);
            GUILayout.Label("Passos restantes: " + selected.Movement.RemainingSteps);
            GUILayout.Label("Última falha: " + selected.Movement.LastFailure);
            GUILayout.Space(8);
            GUILayout.Label("ALIMENTAÇÃO");
            CreatureFeeding feeding = selected.Feeding;
            GUILayout.Label("Reserva: " + (feeding != null && feeding.ReservedItem != null
                ? feeding.ReservedItem.type.ToString() : "nenhuma"));
            GUILayout.Label("Comendo: " + (feeding != null && feeding.IsEating));
            GUILayout.Label("Satisfeita: " + (feeding != null && feeding.IsSatisfied));
            GUILayout.Label("Satisfação restante: " + (feeding != null
                ? feeding.SatisfiedRemaining.ToString("0.0") + "s" : "--"));
            GUILayout.Label("Último resultado: " + (feeding != null
                ? feeding.LastResult : "componente ausente"));
            GUILayout.Space(8);
            GUILayout.Label("DOMESTICAÇÃO (INTERNO)");
            CreatureDomestication domestication = selected.Domestication;
            domestication?.TickRelationship();
            CreatureDirectFeedingTarget directFeeding = selected.DirectFeeding;
            GUILayout.Label("Progresso: " + (domestication != null
                ? domestication.Progress.ToString("0.#") + "%" : "--"));
            GUILayout.Label("Alimentações diretas: " + (domestication != null
                ? domestication.DirectFeedingsReceived.ToString() : "--"));
            GUILayout.Label("Humor interno: " + (domestication != null
                ? domestication.Mood.ToString() : "--"));
            GUILayout.Label("Ciclo registrado: " + (domestication != null
                ? domestication.TrackedDay.ToString() : "--"));
            GUILayout.Label("Dia atual do mundo: "
                + (DayNightCycleManager.Instance != null
                    ? DayNightCycleManager.Instance.CurrentDay.ToString()
                    : "--"));
            GUILayout.Label("Alimentações neste ciclo: "
                + (domestication != null
                    ? domestication.FeedingsThisCycle.ToString() : "--"));
            GUILayout.Label("Mínimo para cuidado: " + (domestication != null
                ? domestication.MinimumFeedingsPerCycle.ToString() : "--"));
            GUILayout.Label("Limite saudável: " + (domestication != null
                ? domestication.HealthyFeedingsPerCycle.ToString() : "--"));
            GUILayout.Label("Máximo aceito: " + (domestication != null
                ? domestication.MaximumFeedingsPerCycle.ToString() : "--"));
            GUILayout.Label("Última alimentação direta: "
                + (domestication != null
                    && domestication.LastDirectFeedingDay > 0
                        ? "ciclo " + domestication.LastDirectFeedingDay
                        : "nenhuma registrada"));
            GUILayout.Label("Cuidado atual cumprido: " + (domestication != null
                && domestication.IsCurrentCycleCareSatisfied));
            GUILayout.Label("Último ciclo válido: " + (domestication != null
                && domestication.HasCompletedCareCycle
                    ? domestication.LastCompletedCycleCareSatisfied.ToString()
                    : "tolerância inicial"));
            GUILayout.Label("Recusa diária: " + (domestication != null
                && domestication.IsRefusing
                    ? "até o próximo ciclo" : "não"));
            GUILayout.Label("Benefício de cuidado ativo: "
                + (domestication != null
                    && domestication.IsDailyCareSatisfiedForBenefits));
            GUILayout.Label("Pedido: " + (directFeeding != null
                && directFeeding.RequestedFood.HasValue
                    ? directFeeding.RequestedFood.Value.ToString()
                    : "nenhum"));
            GUILayout.Label("Resultado: " + (domestication != null
                ? domestication.LastResult : "componente ausente"));
            GUILayout.Label("Pedido/entrega: " + (directFeeding != null
                ? directFeeding.LastRequestResult : "componente ausente"));
            GUILayout.Space(8);
            GUILayout.Label("LEITURA VISUAL (INTERNO)");
            GUILayout.Label("Velocidade: " + (domestication != null
                ? domestication.GetMovementSpeedMultiplier().ToString("0.00") + "x"
                : "1.00x"));
            GUILayout.Label("Cadência de decisão: " + (domestication != null
                ? domestication.GetDecisionIntervalMultiplier().ToString("0.00") + "x"
                : "1.00x"));
            GUILayout.Label("Apresentação: " + (selected.Presentation != null
                ? "ativa" : "componente ausente"));
            GUILayout.Space(8);
            GUILayout.Label("JARDINAGEM TERRITORIAL (INTERNO)");
            CreatureTerritorialGardener gardener =
                selected.TerritorialGardener;
            GUILayout.Label("Configurada: " + (gardener != null
                && gardener.HasAbility));
            GUILayout.Label("Elegível agora: " + (gardener != null
                && gardener.IsEligible));
            GUILayout.Label("Alcance circular: " + (gardener != null
                ? gardener.InteractionRadius.ToString("0.0") + " tiles"
                : "--"));
            GUILayout.Label("Atividade: " + (gardener != null
                ? gardener.ActivityState.ToString() : "--"));
            GUILayout.Label("Alvo atual: " + (gardener != null
                ? gardener.TargetPlanterPosition : "--"));
            GUILayout.Label("Posição de interação: " + (gardener != null
                && gardener.InteractionPosition.HasValue
                    ? gardener.InteractionPosition.Value.ToString()
                    : "--"));
            GUILayout.Label("Cuidado restante: " + (gardener != null
                ? gardener.TendingRemaining.ToString("0.00") + "s" : "--"));
            GUILayout.Label("Cooldown da rodada: " + (gardener != null
                ? gardener.RoundCooldownRemaining.ToString("0.00") + "s"
                : "--"));
            GUILayout.Label("Visitas nesta rodada: " + (gardener != null
                ? gardener.VisitsThisRound.ToString() : "--"));
            GUILayout.Label("Candidatas alcançáveis / inalcançáveis: "
                + (gardener != null
                    ? gardener.ReachableCandidateCount + " / "
                        + gardener.UnreachableCandidateCount
                    : "--"));
            GUILayout.Label("Última plantadeira: " + (gardener != null
                ? gardener.LastAffectedPlanter : "--"));
            GUILayout.Label("Último bônus: " + (gardener != null
                ? gardener.LastAppliedBonusPercent.ToString("0.#") + "%"
                : "--"));
            GUILayout.Label("Duração aplicada: " + (gardener != null
                ? gardener.LastAppliedDuration.ToString("0.0") + "s"
                : "--"));
            GUILayout.Label("Último efeito ativo: " + (gardener != null
                && gardener.LastEffectActive));
            GUILayout.Label("Tempo restante do efeito: " + (gardener != null
                ? gardener.LastEffectRemaining.ToString("0.00") + "s"
                : "--"));
            GUILayout.Label("Resultado: " + (gardener != null
                ? gardener.LastResult : "componente ausente"));
            DrawDirectFeedingButtons(selected);

            if (GUILayout.Button(selected.Brain.DecisionsPaused
                ? "Retomar decisões" : "Pausar decisões"))
                selected.Brain.DecisionsPaused = !selected.Brain.DecisionsPaused;
            if (GUILayout.Button("Forçar nova decisão")) selected.Brain.ForceDecision();
            if (GUILayout.Button("Cancelar movimento")) selected.Movement.CancelMovement();
            if (GUILayout.Button("Limpar alvo")) selected.ClearTarget();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static void DrawDirectFeedingButtons(CreatureController creature)
    {
        CreatureTamingProfileSO profile = creature.Definition != null
            ? creature.Definition.tamingProfile
            : null;
        CreatureDirectFeedingTarget target = creature.DirectFeeding;
        if (profile == null || target == null)
        {
            GUILayout.Label("Taming Profile não configurado.");
            return;
        }

        for (int i = 0; i < profile.acceptedDirectFoods.Count; i++)
        {
            DirectFeedingRule rule = profile.acceptedDirectFoods[i];
            if (rule != null && GUILayout.Button(
                "Solicitar " + rule.resourceType + " (teste)"))
            {
                target.RequestFeeding(rule.resourceType);
            }
        }

        if (target.HasPendingRequest && GUILayout.Button("Cancelar alimentação direta"))
            target.CancelRequest();
    }

    private void DrawSelectionButtons()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Anterior")) SelectRelative(-1);
        if (GUILayout.Button("Próxima")) SelectRelative(1);
        GUILayout.EndHorizontal();
    }

    private void SelectRelative(int direction)
    {
        var creatures = CreatureRegistry.Creatures;
        if (creatures.Count == 0) { selected = null; return; }
        int index = -1;
        if (selected != null)
        {
            for (int i = 0; i < creatures.Count; i++)
            {
                if (creatures[i] == selected) { index = i; break; }
            }
        }
        index = (index + direction + creatures.Count) % creatures.Count;
        selected = creatures[index];
    }
}
