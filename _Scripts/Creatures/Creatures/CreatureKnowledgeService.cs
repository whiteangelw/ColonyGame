using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreatureFoodKnowledgeResult
{
    Accepted = 1,
    Rejected = 2
}

public readonly struct CreatureFoodDiscovery
{
    public readonly ResourceType ResourceType;
    public readonly CreatureFoodKnowledgeResult Result;

    public CreatureFoodDiscovery(
        ResourceType resourceType,
        CreatureFoodKnowledgeResult result)
    {
        ResourceType = resourceType;
        Result = result;
    }
}

[DisallowMultipleComponent]
public sealed class CreatureKnowledgeService : MonoBehaviour
{
    private sealed class SpeciesKnowledge
    {
        public readonly Dictionary<ResourceType, CreatureFoodKnowledgeResult>
            foods = new Dictionary<ResourceType, CreatureFoodKnowledgeResult>();
        public bool observedOverfeeding;
        public bool observedTemporaryRefusal;
    }

    public static CreatureKnowledgeService Instance { get; private set; }

    private readonly Dictionary<string, SpeciesKnowledge> knowledgeBySpecies =
        new Dictionary<string, SpeciesKnowledge>(StringComparer.Ordinal);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            return;
        }

        if (Instance != this) Destroy(gameObject);
    }

    private void OnEnable()
    {
        GameEvents.OnCreatureFeedingObserved += HandleFeedingObserved;
    }

    private void OnDisable()
    {
        GameEvents.OnCreatureFeedingObserved -= HandleFeedingObserved;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void FillFoodDiscoveries(
        string creatureId,
        List<CreatureFoodDiscovery> destination)
    {
        destination.Clear();
        if (!TryGetKnowledge(creatureId, out SpeciesKnowledge knowledge)) return;

        foreach (KeyValuePair<ResourceType, CreatureFoodKnowledgeResult> entry
            in knowledge.foods)
        {
            destination.Add(new CreatureFoodDiscovery(entry.Key, entry.Value));
        }

        destination.Sort((left, right) =>
            ((int)left.ResourceType).CompareTo((int)right.ResourceType));
    }

    public bool HasObservedOverfeeding(string creatureId)
    {
        return TryGetKnowledge(creatureId, out SpeciesKnowledge knowledge)
            && knowledge.observedOverfeeding;
    }

    public bool HasObservedTemporaryRefusal(string creatureId)
    {
        return TryGetKnowledge(creatureId, out SpeciesKnowledge knowledge)
            && knowledge.observedTemporaryRefusal;
    }

    public CreatureKnowledgeSaveData CaptureState()
    {
        CreatureKnowledgeSaveData data = new CreatureKnowledgeSaveData
        {
            hasState = true
        };

        List<string> speciesIds = new List<string>(knowledgeBySpecies.Keys);
        speciesIds.Sort(StringComparer.Ordinal);

        foreach (string speciesId in speciesIds)
        {
            SpeciesKnowledge source = knowledgeBySpecies[speciesId];
            CreatureSpeciesKnowledgeSaveData savedSpecies =
                new CreatureSpeciesKnowledgeSaveData
                {
                    creatureId = speciesId,
                    observedOverfeeding = source.observedOverfeeding,
                    observedTemporaryRefusal = source.observedTemporaryRefusal
                };

            List<ResourceType> foods = new List<ResourceType>(source.foods.Keys);
            foods.Sort((left, right) => ((int)left).CompareTo((int)right));
            foreach (ResourceType food in foods)
            {
                savedSpecies.foods.Add(new CreatureFoodKnowledgeSaveData
                {
                    resourceType = (int)food,
                    result = (int)source.foods[food]
                });
            }

            data.species.Add(savedSpecies);
        }

        return data;
    }

    public void RestoreState(CreatureKnowledgeSaveData data)
    {
        knowledgeBySpecies.Clear();
        if (data == null || !data.hasState || data.species == null)
        {
            GameEvents.TriggerCreatureKnowledgeChanged(string.Empty);
            return;
        }

        foreach (CreatureSpeciesKnowledgeSaveData savedSpecies in data.species)
        {
            string creatureId = NormalizeId(savedSpecies?.creatureId);
            if (string.IsNullOrEmpty(creatureId)) continue;

            SpeciesKnowledge restored = new SpeciesKnowledge
            {
                observedOverfeeding = savedSpecies.observedOverfeeding,
                observedTemporaryRefusal =
                    savedSpecies.observedTemporaryRefusal
            };

            if (savedSpecies.foods != null)
            {
                foreach (CreatureFoodKnowledgeSaveData savedFood
                    in savedSpecies.foods)
                {
                    if (savedFood == null
                        || !Enum.IsDefined(typeof(ResourceType), savedFood.resourceType)
                        || !Enum.IsDefined(
                            typeof(CreatureFoodKnowledgeResult), savedFood.result))
                    {
                        continue;
                    }

                    restored.foods[(ResourceType)savedFood.resourceType] =
                        (CreatureFoodKnowledgeResult)savedFood.result;
                }
            }

            knowledgeBySpecies[creatureId] = restored;
        }

        GameEvents.TriggerCreatureKnowledgeChanged(string.Empty);
    }

    public void ResetKnowledge()
    {
        knowledgeBySpecies.Clear();
        GameEvents.TriggerCreatureKnowledgeChanged(string.Empty);
    }

    private void HandleFeedingObserved(
        string creatureId,
        ResourceType foodType,
        DirectFeedingOutcome outcome)
    {
        creatureId = NormalizeId(creatureId);
        if (string.IsNullOrEmpty(creatureId)) return;

        SpeciesKnowledge knowledge = GetOrCreate(creatureId);
        bool changed = false;

        switch (outcome)
        {
            case DirectFeedingOutcome.Accepted:
                changed |= SetFoodResult(knowledge, foodType,
                    CreatureFoodKnowledgeResult.Accepted);
                break;

            case DirectFeedingOutcome.AcceptedOverLimit:
                changed |= SetFoodResult(knowledge, foodType,
                    CreatureFoodKnowledgeResult.Accepted);
                if (!knowledge.observedOverfeeding)
                {
                    knowledge.observedOverfeeding = true;
                    changed = true;
                }
                break;

            case DirectFeedingOutcome.InvalidFood:
                changed |= SetFoodResult(knowledge, foodType,
                    CreatureFoodKnowledgeResult.Rejected);
                break;

            case DirectFeedingOutcome.Refused:
                if (!knowledge.observedTemporaryRefusal)
                {
                    knowledge.observedTemporaryRefusal = true;
                    changed = true;
                }
                break;
        }

        if (changed) GameEvents.TriggerCreatureKnowledgeChanged(creatureId);
    }

    private SpeciesKnowledge GetOrCreate(string creatureId)
    {
        if (!knowledgeBySpecies.TryGetValue(
            creatureId, out SpeciesKnowledge knowledge))
        {
            knowledge = new SpeciesKnowledge();
            knowledgeBySpecies.Add(creatureId, knowledge);
        }

        return knowledge;
    }

    private bool TryGetKnowledge(
        string creatureId,
        out SpeciesKnowledge knowledge)
    {
        string normalized = NormalizeId(creatureId);
        if (string.IsNullOrEmpty(normalized))
        {
            knowledge = null;
            return false;
        }

        return knowledgeBySpecies.TryGetValue(normalized, out knowledge);
    }

    private static bool SetFoodResult(
        SpeciesKnowledge knowledge,
        ResourceType foodType,
        CreatureFoodKnowledgeResult result)
    {
        if (knowledge.foods.TryGetValue(foodType,
            out CreatureFoodKnowledgeResult existing)
            && existing == result)
        {
            return false;
        }

        knowledge.foods[foodType] = result;
        return true;
    }

    private static string NormalizeId(string creatureId)
    {
        return creatureId?.Trim() ?? string.Empty;
    }
}
