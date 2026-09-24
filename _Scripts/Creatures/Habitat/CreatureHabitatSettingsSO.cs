using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "CreatureHabitatSettings",
    menuName = "Creatures/Habitat Settings")]
public sealed class CreatureHabitatSettingsSO : ScriptableObject
{
    [Header("Identidade")]
    [SerializeField] private string displayName = "Habitat";

    [Header("Ocupação")]
    [SerializeField, Min(1)] private int capacity = 1;
    [Tooltip("Lista explícita. Vazia significa que nenhuma espécie pode usar este habitat.")]
    [SerializeField] private List<CreatureDefinitionSO> compatibleSpecies =
        new List<CreatureDefinitionSO>();

    [Header("Território")]
    [Tooltip("Centro do território relativo à célula principal da estrutura.")]
    [SerializeField] private Vector2Int patrolCenterOffset = new Vector2Int(0, 1);
    [SerializeField, Min(1)] private int patrolRadius = 5;

    [Header("Chegada")]
    [SerializeField, Min(0)] private int arrivalDistance = 1;
    [SerializeField, Min(0.1f)] private float pathRetryDelay = 2f;
    [SerializeField, Min(1)] private int pathAttemptsBeforeRelocation = 3;
    [SerializeField] private bool allowEmergencyRelocation = true;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName.Trim();
    public int Capacity => Mathf.Max(1, capacity);
    public Vector2Int PatrolCenterOffset => patrolCenterOffset;
    public int PatrolRadius => Mathf.Max(1, patrolRadius);
    public int ArrivalDistance => Mathf.Max(0, arrivalDistance);
    public float PathRetryDelay => Mathf.Max(0.1f, pathRetryDelay);
    public int PathAttemptsBeforeRelocation => Mathf.Max(
        1, pathAttemptsBeforeRelocation);
    public bool AllowEmergencyRelocation => allowEmergencyRelocation;

    public bool Allows(CreatureDefinitionSO creatureDefinition)
    {
        if (creatureDefinition == null) return false;

        string creatureId = creatureDefinition.creatureId?.Trim();
        for (int i = 0; i < compatibleSpecies.Count; i++)
        {
            CreatureDefinitionSO candidate = compatibleSpecies[i];
            if (candidate == null) continue;
            if (candidate == creatureDefinition) return true;

            string candidateId = candidate.creatureId?.Trim();
            if (!string.IsNullOrEmpty(creatureId)
                && creatureId == candidateId)
            {
                return true;
            }
        }

        return false;
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        patrolRadius = Mathf.Max(1, patrolRadius);
        arrivalDistance = Mathf.Max(0, arrivalDistance);
        pathRetryDelay = Mathf.Max(0.1f, pathRetryDelay);
        pathAttemptsBeforeRelocation = Mathf.Max(
            1, pathAttemptsBeforeRelocation);
    }
}
