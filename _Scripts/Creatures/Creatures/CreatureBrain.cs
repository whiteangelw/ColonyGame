using UnityEngine;

[RequireComponent(typeof(CreatureController), typeof(CreatureMovement), typeof(CreaturePerception))]
public sealed class CreatureBrain : MonoBehaviour
{
    private CreatureController controller;
    private CreatureMovement movement;
    private CreaturePerception perception;

    public float NextDecisionTime { get; private set; }
    public bool DecisionsPaused { get; set; }

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
        movement = GetComponent<CreatureMovement>();
        perception = GetComponent<CreaturePerception>();
    }

    private void OnEnable() => ScheduleNextDecision(0.1f, 0.5f);

    public void TickDecision()
    {
        if (DecisionsPaused || controller.Definition == null || !controller.IsInitialized)
        {
            ScheduleNextDecision();
            return;
        }

        controller.RefreshGridPosition();
        CreatureDefinitionSO definition = controller.Definition;
        CreatureFeeding feeding = controller.Feeding;
        CreatureDomestication domestication = controller.Domestication;
        domestication?.TickRelationship();

        // Perigo interrompe deslocamento, busca por alimento e até a refeição.
        if (perception.TryFindClosestResource(definition.avoidedResources,
            definition.avoidanceRadius, out ResourceItem danger))
        {
            if (movement.IsMoving && controller.State == CreatureState.Avoid)
            {
                ScheduleNextDecision();
                return;
            }

            movement.CancelMovement();
            feeding.CancelCurrentFeeding();
            if (TryStartAvoidanceMovement(danger, out Vector2Int away))
            {
                controller.SetDecision(CreatureState.Avoid,
                    "Afastando-se de " + danger.type, away);
            }
            else
            {
                controller.SetDecision(CreatureState.Idle,
                    "Não encontrou rota alcançável para se afastar");
            }
            ScheduleNextDecision();
            return;
        }

        if (feeding.IsEating || movement.IsMoving)
        {
            ScheduleNextDecision();
            return;
        }

        if (controller.DirectFeeding != null
            && controller.DirectFeeding.HasPendingRequest)
        {
            controller.SetDecision(CreatureState.Idle,
                "Aguardando alimentação direta");
            ScheduleNextDecision();
            return;
        }

        if (domestication != null && domestication.IsOverfed)
        {
            feeding.CancelCurrentFeeding();
            if (TryStartWanderMovement(out Vector2Int discomfortTarget))
            {
                controller.SetDecision(CreatureState.Uncomfortable,
                    "Inquieta após excesso de alimentação", discomfortTarget);
            }
            else
            {
                controller.SetDecision(CreatureState.Uncomfortable,
                    "Evitando novas interações após excesso");
            }
            ScheduleNextDecision();
            return;
        }

        if (domestication != null && domestication.IsRejectingOffering)
        {
            feeding.CancelCurrentFeeding();
            if (TryStartWanderMovement(out Vector2Int rejectionTarget))
            {
                controller.SetDecision(CreatureState.Uncomfortable,
                    "Rejeitou uma oferta e mantém distância", rejectionTarget);
            }
            else
            {
                controller.SetDecision(CreatureState.Uncomfortable,
                    "Sem interesse na oferta recebida");
            }
            ScheduleNextDecision();
            return;
        }

        if (controller.HabitatLink != null
            && controller.HabitatLink.HandleDecision())
        {
            ScheduleNextDecision();
            return;
        }

        if (feeding.ReservedItem != null && !feeding.HasValidReservation())
            feeding.CancelCurrentFeeding();

        if (feeding.HasValidReservation())
        {
            Vector2Int target = feeding.ReservedItem.GridPosition;
            if (Manhattan(controller.GridPosition, target) <= 1)
            {
                if (!feeding.TryBeginEating())
                {
                    feeding.CancelCurrentFeeding();
                    controller.SetDecision(CreatureState.Idle,
                        "Não conseguiu iniciar a alimentação", target);
                }
            }
            else
            {
                controller.SetDecision(CreatureState.Investigate,
                    "Indo comer " + feeding.ReservedItem.type, target);
                if (!movement.TryMoveTo(target))
                {
                    feeding.CancelCurrentFeeding();
                    controller.SetDecision(CreatureState.Idle,
                        "Alimento reservado, mas sem caminho", target);
                }
            }
            ScheduleNextDecision();
            return;
        }

        if (!feeding.IsSatisfied
            && perception.TryFindClosestAvailableFood(
                definition.attractedResources,
                definition.attractionRadius,
                feeding,
                out ResourceItem attraction)
            && Manhattan(controller.HomePosition, attraction.GridPosition)
                <= definition.territoryRadius
            && feeding.TryReserve(attraction))
        {
            Vector2Int target = attraction.GridPosition;
            if (Manhattan(controller.GridPosition, target) <= 1)
            {
                feeding.TryBeginEating();
            }
            else
            {
                controller.SetDecision(CreatureState.Investigate,
                    "Reservou alimento " + attraction.type, target);
                if (!movement.TryMoveTo(target))
                {
                    feeding.CancelCurrentFeeding();
                    controller.SetDecision(CreatureState.Idle,
                        "Alimento detectado, mas sem caminho", target);
                }
            }
            ScheduleNextDecision();
            return;
        }

        // Jardinagem em andamento precisa concluir a chegada antes da regra de
        // retorno ao território. Sem alvo válido, o componente devolve false e
        // o retorno/patrulhamento seguem normalmente.
        if (controller.TerritorialGardener != null
            && controller.TerritorialGardener.HandleDecision())
        {
            ScheduleNextDecision(0.1f, 0.25f);
            return;
        }

        // Uma atração nunca deve arrastar a criatura indefinidamente para fora
        // de seu território. Assets da versão anterior podem já estar fora;
        // nesse caso, o comportamento prioritário é regressar.
        if (Manhattan(controller.GridPosition, controller.HomePosition)
            > definition.territoryRadius)
        {
            controller.SetDecision(CreatureState.Wander,
                "Retornando ao território", controller.HomePosition);
            if (!movement.TryMoveTo(controller.HomePosition))
                controller.SetDecision(CreatureState.Idle,
                    "Não encontrou rota de volta ao território");
            ScheduleNextDecision();
            return;
        }

        if (TryStartWanderMovement(out Vector2Int wanderTarget))
        {
            bool neglected = domestication != null
                && domestication.ShouldSignalNeglect;
            controller.SetDecision(
                neglected ? CreatureState.Neglected : CreatureState.Wander,
                neglected
                    ? "Mantendo distância após período sem cuidado"
                    : "Explorando o território",
                wanderTarget);
        }
        else
        {
            controller.SetDecision(CreatureState.Idle,
                "Nenhum destino válido no território");
        }

        ScheduleNextDecision();
    }

    public void ForceDecision()
    {
        NextDecisionTime = 0f;
        DecisionsPaused = false;
    }

    private bool TryStartWanderMovement(out Vector2Int target)
    {
        CreatureDefinitionSO definition = controller.Definition;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int x = controller.HomePosition.x + Random.Range(
                -definition.wanderRadius,
                definition.wanderRadius + 1);
            int randomOffset = Random.Range(-3, 4);

            for (int step = 0; step <= 6; step++)
            {
                int verticalOffset = step == 0
                    ? randomOffset
                    : randomOffset + (step % 2 == 1
                        ? (step + 1) / 2
                        : -(step / 2));
                Vector2Int candidate = new Vector2Int(
                    x,
                    controller.HomePosition.y + verticalOffset);

                if (candidate != controller.GridPosition
                    && IsValidTerritoryTarget(candidate)
                    && movement.TryMoveTo(candidate))
                {
                    target = candidate;
                    return true;
                }
            }
        }

        target = controller.GridPosition;
        return false;
    }

    private bool TryStartAvoidanceMovement(
        ResourceItem danger,
        out Vector2Int selectedTarget)
    {
        Vector2Int source = danger.GridPosition;
        int currentDistance = Manhattan(controller.GridPosition, source);
        int preferredDirection = controller.GridPosition.x >= source.x ? 1 : -1;

        // Primeiro tenta os pontos mais distantes, incluindo pequenas diferenças
        // de altura. Cada candidato só é aceito se o A* realmente encontrar rota.
        for (int horizontalDistance = 4; horizontalDistance >= 1; horizontalDistance--)
        {
            for (int verticalOffset = -3; verticalOffset <= 3; verticalOffset++)
            {
                Vector2Int candidate = controller.GridPosition + new Vector2Int(
                    preferredDirection * horizontalDistance,
                    verticalOffset);

                if (Manhattan(candidate, source) <= currentDistance
                    || !IsValidAvoidanceTarget(candidate))
                    continue;

                if (movement.TryMoveTo(candidate))
                {
                    selectedTarget = candidate;
                    return true;
                }
            }
        }

        // Se o lado ideal estiver bloqueado, permite qualquer rota que aumente
        // a distância. Isso evita congelar a criatura junto ao estímulo.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2Int candidate = controller.GridPosition + new Vector2Int(
                Random.Range(-4, 5), Random.Range(-3, 4));
            if (Manhattan(candidate, source) <= currentDistance
                || !IsValidAvoidanceTarget(candidate))
                continue;

            if (movement.TryMoveTo(candidate))
            {
                selectedTarget = candidate;
                return true;
            }
        }

        selectedTarget = controller.GridPosition;
        return false;
    }

    private bool IsValidTerritoryTarget(Vector2Int candidate)
    {
        if (GridManager.Instance == null || NavGraphGenerator.Instance == null)
            return false;
        if (!GridManager.Instance.IsInsideGrid(candidate.x, candidate.y)) return false;
        int territoryRadius = controller.HabitatLink != null
            && controller.HabitatLink.IsBound
                ? controller.HabitatLink.Habitat.PatrolRadius
                : controller.Definition.territoryRadius;
        if (Manhattan(controller.HomePosition, candidate) > territoryRadius)
            return false;
        return NavGraphGenerator.Instance.IsNavigablePosition(candidate.x, candidate.y);
    }

    private bool IsValidAvoidanceTarget(Vector2Int candidate)
    {
        if (GridManager.Instance == null || NavGraphGenerator.Instance == null)
            return false;
        if (!GridManager.Instance.IsInsideGrid(candidate.x, candidate.y)) return false;

        // Fuga pode ultrapassar temporariamente o território. O limite cresce
        // apenas o necessário quando uma criatura da versão anterior já foi
        // atraída para fora dele.
        int currentHomeDistance = Manhattan(
            controller.HomePosition, controller.GridPosition);
        int allowedHomeDistance = Mathf.Max(
            controller.Definition.territoryRadius
                + controller.Definition.avoidanceRadius,
            currentHomeDistance + 2);
        if (Manhattan(controller.HomePosition, candidate) > allowedHomeDistance)
            return false;

        return NavGraphGenerator.Instance.IsNavigablePosition(
            candidate.x, candidate.y);
    }

    private void ScheduleNextDecision(float minimum = -1f, float maximum = -1f)
    {
        CreatureDefinitionSO definition = controller.Definition;
        float min = minimum >= 0f ? minimum
            : definition != null ? definition.minimumDecisionInterval : 1f;
        float max = maximum >= 0f ? maximum
            : definition != null ? definition.maximumDecisionInterval : min;
        float modifier = controller.Domestication != null
            ? controller.Domestication.GetDecisionIntervalMultiplier()
            : 1f;
        min *= modifier;
        max *= modifier;
        NextDecisionTime = Time.time + Random.Range(min, Mathf.Max(min, max));
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
}
