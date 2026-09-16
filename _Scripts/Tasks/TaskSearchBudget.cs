using UnityEngine;

public sealed class TaskSearchBudget
{
    private int budgetFrame = -1;
    private int searchesThisFrame;

    public bool TryAcquire(int maximumSearchesPerFrame)
    {
        if (budgetFrame != Time.frameCount)
        {
            budgetFrame = Time.frameCount;
            searchesThisFrame = 0;
        }

        if (searchesThisFrame >= maximumSearchesPerFrame)
        {
            return false;
        }

        searchesThisFrame++;
        return true;
    }

    public void Reset()
    {
        budgetFrame = -1;
        searchesThisFrame = 0;
    }
}