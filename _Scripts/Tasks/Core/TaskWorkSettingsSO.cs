using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TaskWorkSettings",
    menuName = "Colony/Tasks/Work Settings")]
public sealed class TaskWorkSettingsSO : ScriptableObject
{
    [Serializable]
    private sealed class Entry
    {
        public TaskType taskType;
        [Min(0.1f)] public float workRequired = 1f;
        public bool preserveOnInterruption;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    public float GetWorkRequired(TaskType taskType)
    {
        Entry entry = Find(taskType);
        return entry != null ? Mathf.Max(0.1f, entry.workRequired) : 1f;
    }

    public bool PreservesProgress(TaskType taskType)
    {
        Entry entry = Find(taskType);
        return entry != null && entry.preserveOnInterruption;
    }

    private Entry Find(TaskType taskType)
    {
        return entries.Find(entry => entry != null
            && entry.taskType == taskType);
    }
}
