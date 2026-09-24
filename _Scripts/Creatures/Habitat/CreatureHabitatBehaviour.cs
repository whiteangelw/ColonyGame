using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ConfiguredStructure))]
public sealed class CreatureHabitatBehaviour : MonoBehaviour,
    IConfiguredStructureBehaviour
{
    [SerializeField] private CreatureHabitatSettingsSO settings;

    [Header("Diagnóstico runtime")]
    [SerializeField] private string runtimeStatus = "Não inicializado";

    private readonly HashSet<CreatureController> occupants =
        new HashSet<CreatureController>();
    private ConfiguredStructure structure;
    private bool initialized;

    public bool UsesSpecializedSaveData => false;
    public CreatureHabitatSettingsSO Settings => settings;
    public bool IsInitialized => initialized;
    public int Capacity => settings != null ? settings.Capacity : 0;
    public int OccupantCount
    {
        get
        {
            RemoveInvalidOccupants();
            return occupants.Count;
        }
    }
    public bool HasAvailableSlot => initialized
        && settings != null
        && OccupantCount < Capacity;
    public Vector2Int GridPosition => structure != null
        ? structure.GridPosition
        : Vector2Int.zero;
    public Vector2Int PatrolCenter => GridPosition
        + (settings != null ? settings.PatrolCenterOffset : Vector2Int.zero);
    public int PatrolRadius => settings != null ? settings.PatrolRadius : 1;
    public string DisplayName => settings != null
        ? settings.DisplayName
        : "Habitat sem configuração";

    public void InitializeStructureBehaviour(ConfiguredStructure owner)
    {
        structure = owner;
        initialized = owner != null && settings != null;

        if (!initialized)
        {
            runtimeStatus = settings == null
                ? "Settings não atribuído"
                : "Estrutura não atribuída";
            Debug.LogWarning(
                "[CreatureHabitat] Habitat sem configuração válida.",
                this);
            return;
        }

        CreatureHabitatRegistry.Register(this);
        RefreshRuntimeStatus();
    }

    public void ShutdownStructureBehaviour()
    {
        CreatureHabitatRegistry.Unregister(this);
        ReleaseAllOccupants();
        initialized = false;
        structure = null;
        runtimeStatus = "Desativado";
    }

    private void OnDisable()
    {
        if (!initialized) return;
        CreatureHabitatRegistry.Unregister(this);
        ReleaseAllOccupants();
    }

    private void OnEnable()
    {
        if (initialized) CreatureHabitatRegistry.Register(this);
    }

    public bool CanAccept(CreatureController creature)
    {
        if (!HasAvailableSlot || creature == null
            || creature.Definition == null
            || creature.Domestication == null
            || !creature.Domestication.IsDomesticated)
        {
            return false;
        }

        return settings.Allows(creature.Definition);
    }

    public bool TryAttach(CreatureController creature)
    {
        if (creature == null) return false;
        if (occupants.Contains(creature)) return true;
        if (!CanAccept(creature)) return false;

        occupants.Add(creature);
        RefreshRuntimeStatus();
        return true;
    }

    public void Detach(CreatureController creature)
    {
        if (creature == null || !occupants.Remove(creature)) return;
        RefreshRuntimeStatus();
    }

    public bool Contains(CreatureController creature)
    {
        return creature != null && occupants.Contains(creature);
    }

    private void ReleaseAllOccupants()
    {
        if (occupants.Count == 0) return;

        List<CreatureController> snapshot =
            new List<CreatureController>(occupants);
        for (int i = 0; i < snapshot.Count; i++)
        {
            CreatureController creature = snapshot[i];
            if (creature != null)
                creature.HabitatLink?.NotifyHabitatUnavailable(this);
        }

        occupants.Clear();
        RefreshRuntimeStatus();
    }

    public bool TryGetArrivalPoint(out Vector2Int arrivalPoint)
    {
        arrivalPoint = PatrolCenter;
        if (!initialized || settings == null
            || GridManager.Instance == null
            || NavGraphGenerator.Instance == null)
        {
            return false;
        }

        int radius = settings.PatrolRadius;
        for (int distance = 0; distance <= radius; distance++)
        {
            for (int x = -distance; x <= distance; x++)
            {
                int y = distance - Mathf.Abs(x);
                if (TryUseArrivalCandidate(PatrolCenter + new Vector2Int(x, y),
                    out arrivalPoint)) return true;
                if (y != 0 && TryUseArrivalCandidate(
                    PatrolCenter + new Vector2Int(x, -y), out arrivalPoint))
                    return true;
            }
        }

        return false;
    }

    private static bool TryUseArrivalCandidate(
        Vector2Int candidate,
        out Vector2Int arrivalPoint)
    {
        arrivalPoint = candidate;
        return GridManager.Instance.IsInsideGrid(candidate.x, candidate.y)
            && NavGraphGenerator.Instance.IsNavigablePosition(
                candidate.x, candidate.y);
    }

    [ContextMenu("Log Habitat State")]
    private void LogHabitatState()
    {
        RefreshRuntimeStatus();
        Debug.Log(
            $"[CreatureHabitat] {runtimeStatus}; registrados={CreatureHabitatRegistry.Count}",
            this);
    }

    private void RemoveInvalidOccupants()
    {
        occupants.RemoveWhere(creature => creature == null);
    }

    private void RefreshRuntimeStatus()
    {
        runtimeStatus = initialized
            ? $"Registrado | Ocupação {OccupantCount}/{Capacity} | Centro {PatrolCenter}"
            : "Não inicializado";
    }

    private void OnDrawGizmosSelected()
    {
        if (settings == null || GridManager.Instance == null) return;

        Vector2Int anchor = Application.isPlaying && structure != null
            ? structure.GridPosition
            : GridManager.Instance.WorldToGridPosition(transform.position);
        Vector2Int center = anchor + settings.PatrolCenterOffset;
        Vector3 worldCenter = GridManager.Instance.GridToWorldPosition(center);
        float radius = settings.PatrolRadius * GridManager.Instance.cellSize;

        Gizmos.color = new Color(0.35f, 1f, 0.65f, 0.75f);
        Gizmos.DrawWireSphere(worldCenter, radius);
    }
}
