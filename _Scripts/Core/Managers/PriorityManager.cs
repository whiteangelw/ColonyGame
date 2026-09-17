using System.Collections.Generic;
using UnityEngine;

public class PriorityManager : MonoBehaviour
{
    public static PriorityManager Instance { get; private set; }

    private readonly Dictionary<TaskType, int> categoryPriorities = new Dictionary<TaskType, int>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializeDefaultPriorities();
    }

    private void InitializeDefaultPriorities()
    {
        categoryPriorities[TaskType.Dig] = 5;
        categoryPriorities[TaskType.BuildTile] = 5;
        categoryPriorities[TaskType.HaulResource] = 5;
        categoryPriorities[TaskType.Dismantle] = 5;
        categoryPriorities[TaskType.Harvest] = 5;
    }

    public void SetCategoryPriority(TaskType type, int priority)
    {
        int clampedPriority = Mathf.Clamp(priority, 1, 9);
        categoryPriorities[type] = clampedPriority;

        GameEvents.TriggerPriorityChanged(type, clampedPriority);
        Debug.Log($"[PriorityManager] Categoria {type} atualizada para prioridade: {clampedPriority}");
    }

    public int GetCategoryPriority(TaskType type)
    {
        if (categoryPriorities.TryGetValue(type, out int priority))
        {
            return priority;
        }
        return 5;
    }
}
