using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DuplicantSelectionService : Singleton<DuplicantSelectionService>
{
    private readonly HashSet<DuplicantSelectionTarget> selectedTargets =
        new HashSet<DuplicantSelectionTarget>();

    public int SelectedCount => selectedTargets.Count;
    public bool IsOverviewEnabled { get; private set; }
    public event Action<bool> OverviewChanged;

    public void ToggleOverview()
    {
        SetOverviewEnabled(!IsOverviewEnabled);
    }

    public void SetOverviewEnabled(bool enabled)
    {
        if (IsOverviewEnabled == enabled) return;
        IsOverviewEnabled = enabled;
        OverviewChanged?.Invoke(enabled);
    }

    public void Select(DuplicantSelectionTarget target, bool additive)
    {
        if (target == null) return;

        if (!additive)
        {
            ClearSelectionExcept(target);

            if (selectedTargets.Add(target))
                target.SetSelectedFromService(true);

            return;
        }

        if (selectedTargets.Remove(target))
        {
            target.SetSelectedFromService(false);
            return;
        }

        selectedTargets.Add(target);
        target.SetSelectedFromService(true);
    }

    public void Deselect(DuplicantSelectionTarget target)
    {
        if (target == null || !selectedTargets.Remove(target)) return;
        target.SetSelectedFromService(false);
    }

    public void ClearSelection()
    {
        DuplicantSelectionTarget[] snapshot =
            new DuplicantSelectionTarget[selectedTargets.Count];
        selectedTargets.CopyTo(snapshot);
        selectedTargets.Clear();

        foreach (DuplicantSelectionTarget target in snapshot)
        {
            if (target != null)
                target.SetSelectedFromService(false);
        }
    }

    private void ClearSelectionExcept(DuplicantSelectionTarget exception)
    {
        DuplicantSelectionTarget[] snapshot =
            new DuplicantSelectionTarget[selectedTargets.Count];
        selectedTargets.CopyTo(snapshot);

        foreach (DuplicantSelectionTarget target in snapshot)
        {
            if (target == exception) continue;
            selectedTargets.Remove(target);

            if (target != null)
                target.SetSelectedFromService(false);
        }
    }
}
