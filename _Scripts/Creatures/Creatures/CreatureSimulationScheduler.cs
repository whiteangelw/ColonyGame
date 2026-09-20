using System.Collections;
using UnityEngine;

public sealed class CreatureSimulationScheduler : Singleton<CreatureSimulationScheduler>
{
    [SerializeField, Min(0.02f)] private float schedulerInterval = 0.1f;
    [SerializeField, Min(1)] private int maximumDecisionsPerPass = 8;
    private Coroutine schedulerRoutine;
    private int nextCreatureIndex;

    private void OnEnable()
    {
        if (schedulerRoutine == null)
            schedulerRoutine = StartCoroutine(SchedulerRoutine());
    }

    private void OnDisable()
    {
        if (schedulerRoutine != null) StopCoroutine(schedulerRoutine);
        schedulerRoutine = null;
    }

    private IEnumerator SchedulerRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(schedulerInterval);
            var creatures = CreatureRegistry.Creatures;
            if (creatures.Count == 0) continue;

            int checkedCount = 0;
            int decisions = 0;
            while (checkedCount < creatures.Count && decisions < maximumDecisionsPerPass)
            {
                if (nextCreatureIndex >= creatures.Count) nextCreatureIndex = 0;
                CreatureController creature = creatures[nextCreatureIndex++];
                checkedCount++;
                if (creature == null || creature.Brain == null
                    || Time.time < creature.Brain.NextDecisionTime) continue;
                creature.Brain.TickDecision();
                decisions++;
            }
        }
    }
}
