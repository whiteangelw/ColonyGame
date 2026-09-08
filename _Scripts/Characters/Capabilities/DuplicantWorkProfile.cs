using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NewDuplicantWorkProfile",
    menuName = "Duplicants/Work Profile"
)]
public class DuplicantWorkProfile : ScriptableObject
{
    [Serializable]
    public class TaskAffinity
    {
        public TaskType taskType;

        [Tooltip("-2 evita este trabalho quando houver alternativas; +2 o prefere. Nenhum valor bloqueia o trabalho.")]
        [Range(-2, 2)]
        public int preference;
    }

    [SerializeField] private List<TaskAffinity> taskAffinities =
        new List<TaskAffinity>();

    public int GetAffinity(TaskType taskType)
    {
        foreach (TaskAffinity affinity in taskAffinities)
        {
            if (affinity != null && affinity.taskType == taskType)
            {
                return Mathf.Clamp(affinity.preference, -2, 2);
            }
        }

        return 0;
    }
}
