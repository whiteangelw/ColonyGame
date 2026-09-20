using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CreatureDefinition", menuName = "Creatures/Definition")]
public sealed class CreatureDefinitionSO : ScriptableObject
{
    [Header("Identidade")]
    public string creatureId = "creature.new";
    public string displayName = "Criatura";
    public Sprite sprite;
    [Tooltip("Prefab usado pelo CreatureSpawnService.")]
    public GameObject prefab;
    public CreatureNavigationProfileSO navigationProfile;
    public CreatureTamingProfileSO tamingProfile;

    [Header("Movimento e decisões")]
    [Min(0.1f)] public float moveSpeed = 2f;
    [Min(0.1f)] public float minimumDecisionInterval = 0.6f;
    [Min(0.1f)] public float maximumDecisionInterval = 1.2f;
    [Min(1)] public int territoryRadius = 8;
    [Min(1)] public int wanderRadius = 5;

    [Header("Percepção")]
    [Min(1)] public int attractionRadius = 8;
    [Min(1)] public int avoidanceRadius = 5;
    public List<ResourceType> attractedResources = new List<ResourceType>();
    public List<ResourceType> avoidedResources = new List<ResourceType>();

    [Header("Alimentação no chão")]
    [Tooltip("Tempo, em segundos, que a criatura leva para consumir uma porção.")]
    [Min(0.1f)] public float eatingDuration = 2f;
    [Tooltip("Durante este tempo a criatura não procura outro alimento atraente.")]
    [Min(0f)] public float satisfiedDuration = 25f;

    private void OnValidate()
    {
        maximumDecisionInterval = Mathf.Max(minimumDecisionInterval, maximumDecisionInterval);
        wanderRadius = Mathf.Min(wanderRadius, territoryRadius);
    }
}
