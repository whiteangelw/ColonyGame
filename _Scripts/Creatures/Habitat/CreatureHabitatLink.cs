using UnityEngine;

public enum CreatureHabitatLinkState
{
    Unbound,
    Reserved,
    Travelling,
    WaitingForPath,
    Bound,
    EmergencyRelocation
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CreatureController), typeof(CreatureMovement))]
public sealed class CreatureHabitatLink : MonoBehaviour
{
    private CreatureController controller;
    private CreatureMovement movement;
    private CreatureHabitatBehaviour habitat;
    private Vector2Int? pendingRestorePosition;
    private Vector2Int? arrivalTarget;
    private float nextPathAttemptAt;
    private float restoreDeadline;

    public CreatureHabitatLinkState State { get; private set; } =
        CreatureHabitatLinkState.Unbound;
    public CreatureHabitatBehaviour Habitat => habitat;
    public bool IsBound => State == CreatureHabitatLinkState.Bound
        && HasValidHabitat;
    public bool HasAssignedHabitat => HasValidHabitat;
    public int FailedPathAttempts { get; private set; }
    public string LastResult { get; private set; } = "Sem habitat";
    public string HabitatName => HasValidHabitat
        ? habitat.DisplayName
        : pendingRestorePosition.HasValue ? "Aguardando restauração" : "nenhum";
    public Vector2Int HabitatGridPosition => HasValidHabitat
        ? habitat.GridPosition
        : pendingRestorePosition ?? Vector2Int.zero;

    private bool HasValidHabitat => habitat != null
        && habitat.IsInitialized
        && habitat.Contains(controller);

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
        movement = GetComponent<CreatureMovement>();
    }

    private void OnDisable()
    {
        ReleaseHabitat("Criatura desativada");
    }

    public bool HandleDecision()
    {
        if (controller.Domestication == null
            || !controller.Domestication.IsDomesticated)
        {
            return false;
        }

        ValidateAssignment();
        TryRestorePendingAssignment();

        if (!HasValidHabitat && pendingRestorePosition.HasValue)
        {
            if (Time.time < restoreDeadline) return true;
            pendingRestorePosition = null;
            controller.RestoreNaturalHome();
            LastResult = "Habitat salvo não foi encontrado; procurando outro";
        }

        if (!HasValidHabitat && !TryReserveNearestHabitat())
        {
            State = CreatureHabitatLinkState.Unbound;
            LastResult = "Nenhum habitat compatível com vaga";
            return false;
        }

        // Depois de confirmado, o habitat apenas define a nova área de
        // patrulhamento. A criatura não deve voltar ao centro a cada decisão.
        if (IsBound) return false;

        if (IsInsideArrivalRange())
        {
            CompleteBinding("Chegou ao território do habitat");
            return false;
        }

        if (movement.IsMoving) return true;
        if (Time.time < nextPathAttemptAt) return true;

        TryTravelOrRelocate();
        return true;
    }

    public void RestoreAssignment(Vector2Int habitatPosition)
    {
        pendingRestorePosition = habitatPosition;
        restoreDeadline = Time.time + 5f;
        State = CreatureHabitatLinkState.Reserved;
        LastResult = "Aguardando restauração do habitat";
    }

    public void NotifyHabitatUnavailable(
        CreatureHabitatBehaviour unavailableHabitat)
    {
        if (unavailableHabitat == null || habitat != unavailableHabitat)
            return;

        movement.CancelMovement();
        unavailableHabitat.Detach(controller);
        habitat = null;
        arrivalTarget = null;
        pendingRestorePosition = null;
        FailedPathAttempts = 0;
        nextPathAttemptAt = 0f;
        controller.RestoreNaturalHome();
        State = CreatureHabitatLinkState.Unbound;
        LastResult = "Habitat removido; território natural restaurado";
        controller.SetDecision(
            CreatureState.Idle,
            LastResult,
            controller.NaturalHomePosition);
        controller.Brain?.ForceDecision();
    }

    private bool TryReserveNearestHabitat()
    {
        if (!CreatureHabitatRegistry.TryFindNearestAvailable(
            controller, out CreatureHabitatBehaviour selected))
        {
            return false;
        }

        return TryAssign(selected, "Vaga reservada");
    }

    private bool TryAssign(CreatureHabitatBehaviour selected, string result)
    {
        if (selected == null || !selected.TryAttach(controller)) return false;

        habitat = selected;
        pendingRestorePosition = null;
        arrivalTarget = null;
        FailedPathAttempts = 0;
        nextPathAttemptAt = 0f;
        State = CreatureHabitatLinkState.Reserved;
        LastResult = result;
        return true;
    }

    private void TryRestorePendingAssignment()
    {
        if (HasValidHabitat || !pendingRestorePosition.HasValue) return;

        if (CreatureHabitatRegistry.TryGetAt(
            pendingRestorePosition.Value,
            out CreatureHabitatBehaviour restored))
        {
            if (!TryAssign(restored, "Vínculo restaurado do save"))
            {
                pendingRestorePosition = null;
                LastResult = "Habitat salvo não possui vaga compatível";
            }
        }
    }

    private void TryTravelOrRelocate()
    {
        if (!habitat.TryGetArrivalPoint(out Vector2Int target))
        {
            RegisterPathFailure("Habitat sem célula válida de chegada", null);
            return;
        }

        arrivalTarget = target;

        controller.SetDecision(
            CreatureState.Wander,
            "Indo para o habitat " + habitat.DisplayName,
            target);

        if (movement.TryMoveTo(target))
        {
            State = CreatureHabitatLinkState.Travelling;
            LastResult = "Caminhando até " + target;
            return;
        }

        RegisterPathFailure(movement.LastFailure, target);
    }

    private void RegisterPathFailure(string reason, Vector2Int? relocationTarget)
    {
        FailedPathAttempts++;
        nextPathAttemptAt = Time.time + habitat.Settings.PathRetryDelay;
        State = CreatureHabitatLinkState.WaitingForPath;
        LastResult = reason + " | tentativa " + FailedPathAttempts + "/"
            + habitat.Settings.PathAttemptsBeforeRelocation;

        if (FailedPathAttempts < habitat.Settings.PathAttemptsBeforeRelocation
            || !habitat.Settings.AllowEmergencyRelocation)
        {
            return;
        }

        Vector2Int target;
        if (relocationTarget.HasValue)
            target = relocationTarget.Value;
        else if (!habitat.TryGetArrivalPoint(out target))
            return;

        State = CreatureHabitatLinkState.EmergencyRelocation;
        arrivalTarget = target;
        controller.RelocateTo(target);
        CompleteBinding("Relocação emergencial após falhas de caminho");
        Debug.LogWarning(
            "[CreatureHabitat] " + controller.name
            + " foi realocada após " + FailedPathAttempts
            + " tentativas sem caminho.", controller);
    }

    private bool IsInsideArrivalRange()
    {
        Vector2Int destination = arrivalTarget ?? habitat.PatrolCenter;
        int distance = Mathf.Abs(controller.GridPosition.x - destination.x)
            + Mathf.Abs(controller.GridPosition.y - destination.y);
        return distance <= habitat.Settings.ArrivalDistance;
    }

    private void CompleteBinding(string result)
    {
        controller.SetHomePosition(habitat.PatrolCenter);
        FailedPathAttempts = 0;
        State = CreatureHabitatLinkState.Bound;
        LastResult = result;
        controller.SetDecision(CreatureState.Idle, result, habitat.PatrolCenter);
    }

    private void ValidateAssignment()
    {
        if (ReferenceEquals(habitat, null)) return;
        if (HasValidHabitat) return;

        movement.CancelMovement();
        // Um objeto Unity destruído compara como null, embora a referência
        // gerenciada ainda exista. Só chamamos Detach quando ele segue vivo.
        if (habitat != null) habitat.Detach(controller);
        habitat = null;
        arrivalTarget = null;
        FailedPathAttempts = 0;
        nextPathAttemptAt = 0f;
        controller.RestoreNaturalHome();
        State = CreatureHabitatLinkState.Unbound;
        LastResult = "Habitat indisponível; território natural restaurado";
    }

    private void ReleaseHabitat(string result)
    {
        if (habitat != null) habitat.Detach(controller);
        habitat = null;
        pendingRestorePosition = null;
        arrivalTarget = null;
        State = CreatureHabitatLinkState.Unbound;
        LastResult = result;
    }
}
