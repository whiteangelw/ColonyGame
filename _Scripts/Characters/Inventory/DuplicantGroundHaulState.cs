using UnityEngine;

/// <summary>
/// Mantém o estado temporário de uma tarefa de transporte do chão ao baú.
/// </summary>
public sealed class DuplicantGroundHaulState
{
    public ResourceItem ActiveItem { get; private set; }

    public ResourceItem ReservedItem { get; private set; }

    public Task AdditionalTask { get; private set; }

    public bool HasCollectedItem { get; private set; }

    public void SetActiveItem(ResourceItem item)
    {
        ActiveItem = item;
    }

    public void MarkItemCollected()
    {
        HasCollectedItem = true;
    }

    public bool TryReserveItem(
        ResourceItem item,
        DuplicantTaskRunner runner)
    {
        if (item == null || runner == null)
        {
            return false;
        }

        if (!item.TryReserve(runner))
        {
            return false;
        }

        ReservedItem = item;
        return true;
    }

    public void TrackAdditionalTask(Task task)
    {
        AdditionalTask = task;
    }

    public void ReleaseItemReservation(
        DuplicantTaskRunner runner)
    {
        if (ReservedItem != null)
        {
            ReservedItem.ReleaseReservation(runner);
        }

        ReservedItem = null;
    }

    public void ReleaseAdditionalTask(bool removeTask = false)
    {
        if (AdditionalTask == null)
        {
            return;
        }

        if (removeTask)
        {
            TaskManager.Instance?.RemoveTask(AdditionalTask);
        }
        else
        {
            TaskManager.Instance?.ReleaseTask(AdditionalTask);
        }

        AdditionalTask = null;
    }

    /// <summary>
    /// Deve ser chamado somente após liberar as reservas externas.
    /// </summary>
    public void Clear(Task activeTask)
    {
        activeTask?.ResetGroundHaulProgress();

        ActiveItem = null;
        ReservedItem = null;
        AdditionalTask = null;
        HasCollectedItem = false;
    }
}