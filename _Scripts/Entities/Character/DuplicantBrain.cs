using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DuplicantController))]
public class DuplicantBrain : MonoBehaviour
{
    private DuplicantController controller;
    private DuplicantMovement movement;
    private DuplicantTaskRunner taskRunner;

    private float searchCooldown = 0f;
    private Coroutine brainCoroutine;
    private Coroutine activeTaskCoroutine;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
        movement = GetComponent<DuplicantMovement>();
        taskRunner = GetComponent<DuplicantTaskRunner>();
    }

    private void OnEnable()
    {
        if (brainCoroutine == null)
        {
            brainCoroutine = StartCoroutine(WorkerBrainRoutine());
        }
    }

    private void OnDisable()
    {
        if (activeTaskCoroutine != null)
        {
            StopCoroutine(activeTaskCoroutine);
        }

        if (brainCoroutine != null)
        {
            StopCoroutine(brainCoroutine);
        }

        brainCoroutine = null;
        activeTaskCoroutine = null;
    }

    /// <summary>
    /// Loop principal de inteligência e tomada de decisão do colono.
    /// </summary>
    private IEnumerator WorkerBrainRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0.0f, 0.5f));
        WaitForSeconds waitInterval = new WaitForSeconds(0.3f);

        while (true)
        {
            yield return waitInterval;

            if (movement.ShouldFall() && controller.currentState != DuplicantController.WorkerState.Falling)
            {
                yield return StartCoroutine(movement.HandleFallingRoutine());
                continue;
            }

            if (searchCooldown > 0f)
            {
                searchCooldown -= 0.3f;
                continue;
            }

            if (controller.currentState == DuplicantController.WorkerState.Idle && controller.currentTask == null && !movement.ShouldFall())
            {
                if (TaskManager.Instance != null)
                {
                    if (!TaskManager.Instance.TryAcquireTaskSearchSlot())
                    {
                        continue;
                    }

                    List<Vector2Int> calculatedPath;
                    Task availableTask = TaskManager.Instance.GetNextTaskFor(
                        controller.gridPosition,
                        out calculatedPath,
                        controller.capabilityProfile,
                        controller.workProfile
                    );

                    if (availableTask != null)
                    {
                        controller.currentTask = availableTask;

                        activeTaskCoroutine = StartCoroutine(
                            ExecuteAssignedTask(
                                controller.currentTask,
                                calculatedPath
                            )
                        );
                    }
                    else
                    {
                        searchCooldown = 1.0f;
                    }
                }
            }
        }
    }

    private IEnumerator ExecuteAssignedTask(
        Task task,
        List<Vector2Int> calculatedPath)
    {
        yield return taskRunner.ExecuteTaskRoutine(task, calculatedPath);
        activeTaskCoroutine = null;
    }

    /// <summary>
    /// Permite definir um cooldown externo para busca de tarefas.
    /// </summary>
    public void SetSearchCooldown(float time)
    {
        searchCooldown = time;
    }

    public void CancelActiveTaskExecution()
    {
        if (activeTaskCoroutine == null)
        {
            return;
        }

        StopCoroutine(activeTaskCoroutine);
        activeTaskCoroutine = null;
        searchCooldown = 0f;
    }

    public void RequestImmediateTaskSearch()
    {
        searchCooldown = 0f;
    }
}
