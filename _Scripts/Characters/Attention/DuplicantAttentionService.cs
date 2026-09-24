using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DuplicantAttentionService :
    Singleton<DuplicantAttentionService>
{
    private readonly Dictionary<DuplicantAttentionSource, DuplicantAttentionReport>
        activeReports =
            new Dictionary<DuplicantAttentionSource, DuplicantAttentionReport>();

    private readonly List<DuplicantAttentionReport> orderedReports =
        new List<DuplicantAttentionReport>();

    private DuplicantAttentionSource lastFocusedSource;

    public int ActiveCount => orderedReports.Count;
    public event Action AttentionChanged;

    private void Start()
    {
        DuplicantAttentionSource[] existingSources =
            FindObjectsByType<DuplicantAttentionSource>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (DuplicantAttentionSource source in existingSources)
            Register(source);
    }

    public void Register(DuplicantAttentionSource source)
    {
        if (source == null) return;
        Refresh(source);
    }

    public void Unregister(DuplicantAttentionSource source)
    {
        if (source == null || !activeReports.Remove(source)) return;
        RebuildOrder();
    }

    public void Refresh(DuplicantAttentionSource source)
    {
        if (source == null) return;

        DuplicantAttentionReport report = source.BuildReport();
        bool changed;

        if (report.Reason == DuplicantAttentionReason.None)
        {
            changed = activeReports.Remove(source);
        }
        else if (!activeReports.TryGetValue(source, out DuplicantAttentionReport old)
            || old.Reason != report.Reason
            || !Mathf.Approximately(old.Severity, report.Severity))
        {
            activeReports[source] = report;
            changed = true;
        }
        else
        {
            changed = false;
        }

        if (changed)
            RebuildOrder();
    }

    public bool TryGetMostSevere(out DuplicantAttentionReport report)
    {
        RemoveInvalidSources();

        if (orderedReports.Count == 0)
        {
            report = default;
            return false;
        }

        report = orderedReports[0];
        return true;
    }

    public bool TryGetNext(out DuplicantAttentionReport report)
    {
        RemoveInvalidSources();

        if (orderedReports.Count == 0)
        {
            report = default;
            return false;
        }

        int currentIndex = FindReportIndex(lastFocusedSource);
        int nextIndex = currentIndex >= 0
            ? (currentIndex + 1) % orderedReports.Count
            : 0;

        report = orderedReports[nextIndex];
        lastFocusedSource = report.Source;
        return true;
    }

    private void RebuildOrder()
    {
        orderedReports.Clear();

        foreach (DuplicantAttentionReport report in activeReports.Values)
        {
            if (report.Source != null && report.Source.isActiveAndEnabled)
                orderedReports.Add(report);
        }

        orderedReports.Sort(CompareReports);

        if (FindReportIndex(lastFocusedSource) < 0)
            lastFocusedSource = null;

        AttentionChanged?.Invoke();
    }

    private void RemoveInvalidSources()
    {
        List<DuplicantAttentionSource> invalid = null;

        foreach (DuplicantAttentionSource source in activeReports.Keys)
        {
            if (source != null && source.isActiveAndEnabled) continue;
            if (invalid == null)
                invalid = new List<DuplicantAttentionSource>();
            invalid.Add(source);
        }

        if (invalid == null) return;

        foreach (DuplicantAttentionSource source in invalid)
            activeReports.Remove(source);

        RebuildOrder();
    }

    private int FindReportIndex(DuplicantAttentionSource source)
    {
        if (source == null) return -1;

        for (int i = 0; i < orderedReports.Count; i++)
        {
            if (orderedReports[i].Source == source)
                return i;
        }

        return -1;
    }

    private static int CompareReports(
        DuplicantAttentionReport left,
        DuplicantAttentionReport right)
    {
        int reasonComparison = right.Reason.CompareTo(left.Reason);
        if (reasonComparison != 0) return reasonComparison;

        int severityComparison = right.Severity.CompareTo(left.Severity);
        if (severityComparison != 0) return severityComparison;

        return 0;
    }
}
