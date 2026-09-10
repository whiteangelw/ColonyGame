using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

public enum PerformanceMetric
{
    Pathfinding,
    TaskSelection,
    LiquidVisualization,
    LiquidSimulation,
    NavGraphFull,
    NavGraphPartial,
    Save,
    Load,
    SaveMenuMetadata,
    SaveReadFile,
    SaveDeserialize,
    SaveCapture,
    SaveSerialize,
    SaveWrite,
    LoadClearWorld,
    LoadRestoreGrid,
    LoadRestoreEntities,
    LoadPostRestore,
    WorldGeneration,
    TilemapFullRefresh,
    FogFullRefresh
}

public readonly struct PerformanceMetricsSnapshot
{
    public readonly float FramesPerSecond;
    public readonly float FrameTimeMs;
    public readonly float PathRequestsPerSecond;
    public readonly float PathfindingAverageMs;
    public readonly float PathfindingMaximumMs;
    public readonly float TaskSelectionAverageMs;
    public readonly float TaskSelectionMaximumMs;
    public readonly int PendingTasks;
    public readonly int ActiveResourceItems;
    public readonly int ActiveDuplicants;
    public readonly float BrainTicksPerSecond;
    public readonly float NeedsTicksPerSecond;
    public readonly int FullNavRegenerations;
    public readonly int PartialNavRegenerations;
    public readonly int NavCellsRegenerated;
    public readonly int GlobalObjectSearches;
    public readonly long GcAllocatedBytesPerFrame;
    public readonly float LastSaveMs;
    public readonly float LastLoadMs;
    public readonly float LiquidVisualizerAverageMs;
    public readonly float LiquidVisualizerMaximumMs;
    public readonly int LiquidCellsProcessed;
    public readonly int LiquidDirtyCellsPending;
    public readonly int LiquidDepthColumnsPending;
    public readonly float LiquidSimulationAverageMs;
    public readonly float LiquidSimulationMaximumMs;
    public readonly float LiquidSimulationTicksPerSecond;
    public readonly float LiquidSimulationCellsPerSecond;
    public readonly int LiquidBufferResizes;
    public readonly float LastSaveMenuMetadataMs;
    public readonly float LastSaveReadFileMs;
    public readonly float LastSaveDeserializeMs;
    public readonly float LastSaveCaptureMs;
    public readonly float LastSaveSerializeMs;
    public readonly float LastSaveWriteMs;
    public readonly float LastLoadClearWorldMs;
    public readonly float LastLoadRestoreGridMs;
    public readonly float LastLoadRestoreEntitiesMs;
    public readonly float LastLoadPostRestoreMs;
    public readonly float LastWorldGenerationMs;
    public readonly float LastTilemapFullRefreshMs;
    public readonly float LastFogFullRefreshMs;

    public PerformanceMetricsSnapshot(
        float fps, float frameMs, float pathRate, float pathAverageMs,
        float pathMaximumMs, float taskAverageMs, float taskMaximumMs,
        int pendingTasks, int activeItems, int activeDuplicants,
        float brainTickRate, float needsTickRate, int fullNavRegenerations,
        int partialNavRegenerations, int navCellsRegenerated,
        int globalObjectSearches, long gcAllocatedBytesPerFrame,
        float lastSaveMs, float lastLoadMs,
        float liquidVisualizerAverageMs, float liquidVisualizerMaximumMs,
        int liquidCellsProcessed, int liquidDirtyCellsPending,
        int liquidDepthColumnsPending,
        float liquidSimulationAverageMs, float liquidSimulationMaximumMs,
        float liquidSimulationTicksPerSecond,
        float liquidSimulationCellsPerSecond,
        int liquidBufferResizes,
        float lastSaveMenuMetadataMs, float lastSaveReadFileMs,
        float lastSaveDeserializeMs, float lastSaveCaptureMs,
        float lastSaveSerializeMs, float lastSaveWriteMs,
        float lastLoadClearWorldMs, float lastLoadRestoreGridMs,
        float lastLoadRestoreEntitiesMs, float lastLoadPostRestoreMs,
        float lastWorldGenerationMs, float lastTilemapFullRefreshMs,
        float lastFogFullRefreshMs)
    {
        FramesPerSecond = fps;
        FrameTimeMs = frameMs;
        PathRequestsPerSecond = pathRate;
        PathfindingAverageMs = pathAverageMs;
        PathfindingMaximumMs = pathMaximumMs;
        TaskSelectionAverageMs = taskAverageMs;
        TaskSelectionMaximumMs = taskMaximumMs;
        PendingTasks = pendingTasks;
        ActiveResourceItems = activeItems;
        ActiveDuplicants = activeDuplicants;
        BrainTicksPerSecond = brainTickRate;
        NeedsTicksPerSecond = needsTickRate;
        FullNavRegenerations = fullNavRegenerations;
        PartialNavRegenerations = partialNavRegenerations;
        NavCellsRegenerated = navCellsRegenerated;
        GlobalObjectSearches = globalObjectSearches;
        GcAllocatedBytesPerFrame = gcAllocatedBytesPerFrame;
        LastSaveMs = lastSaveMs;
        LastLoadMs = lastLoadMs;
        LiquidVisualizerAverageMs = liquidVisualizerAverageMs;
        LiquidVisualizerMaximumMs = liquidVisualizerMaximumMs;
        LiquidCellsProcessed = liquidCellsProcessed;
        LiquidDirtyCellsPending = liquidDirtyCellsPending;
        LiquidDepthColumnsPending = liquidDepthColumnsPending;
        LiquidSimulationAverageMs = liquidSimulationAverageMs;
        LiquidSimulationMaximumMs = liquidSimulationMaximumMs;
        LiquidSimulationTicksPerSecond = liquidSimulationTicksPerSecond;
        LiquidSimulationCellsPerSecond = liquidSimulationCellsPerSecond;
        LiquidBufferResizes = liquidBufferResizes;
        LastSaveMenuMetadataMs = lastSaveMenuMetadataMs;
        LastSaveReadFileMs = lastSaveReadFileMs;
        LastSaveDeserializeMs = lastSaveDeserializeMs;
        LastSaveCaptureMs = lastSaveCaptureMs;
        LastSaveSerializeMs = lastSaveSerializeMs;
        LastSaveWriteMs = lastSaveWriteMs;
        LastLoadClearWorldMs = lastLoadClearWorldMs;
        LastLoadRestoreGridMs = lastLoadRestoreGridMs;
        LastLoadRestoreEntitiesMs = lastLoadRestoreEntitiesMs;
        LastLoadPostRestoreMs = lastLoadPostRestoreMs;
        LastWorldGenerationMs = lastWorldGenerationMs;
        LastTilemapFullRefreshMs = lastTilemapFullRefreshMs;
        LastFogFullRefreshMs = lastFogFullRefreshMs;
    }
}

/// <summary>
/// Coletor agregado de profiling. É criado automaticamente somente no Editor
/// ou em Development Build; builds finais recebem apenas chamadas vazias.
/// </summary>
public sealed class PerformanceMetricsService : MonoBehaviour
{
    public static PerformanceMetricsService Instance { get; private set; }
    public PerformanceMetricsSnapshot Snapshot { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField, Min(0.25f)] private float samplingInterval = 1f;
    [SerializeField] private bool collectionEnabled = true;

    private int sampleFrames;
    private float sampleStartTime;
    private int pathRequests;
    private int pathSamples;
    private double pathTotalMs;
    private double pathMaximumMs;
    private int taskSamples;
    private double taskTotalMs;
    private double taskMaximumMs;
    private int liquidVisualizerSamples;
    private double liquidVisualizerTotalMs;
    private double liquidVisualizerMaximumMs;
    private int liquidCellsProcessed;
    private int liquidDirtyCellsPending;
    private int liquidDepthColumnsPending;
    private int liquidSimulationSamples;
    private double liquidSimulationTotalMs;
    private double liquidSimulationMaximumMs;
    private long liquidSimulationCells;
    private int liquidBufferResizes;
    private int brainTicks;
    private int needsTicks;
    private int fullNavRegenerations;
    private int partialNavRegenerations;
    private int navCellsRegenerated;
    private int globalObjectSearches;
    private int activeResourceItems;
    private int activeDuplicants;
    private float lastSaveMs;
    private float lastLoadMs;
    private float lastSaveMenuMetadataMs;
    private float lastSaveReadFileMs;
    private float lastSaveDeserializeMs;
    private float lastSaveCaptureMs;
    private float lastSaveSerializeMs;
    private float lastSaveWriteMs;
    private float lastLoadClearWorldMs;
    private float lastLoadRestoreGridMs;
    private float lastLoadRestoreEntitiesMs;
    private float lastLoadPostRestoreMs;
    private float lastWorldGenerationMs;
    private float lastTilemapFullRefreshMs;
    private float lastFogFullRefreshMs;
    private ProfilerRecorder gcAllocatedRecorder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        GameObject host = new GameObject("_PerformanceMetrics");
        DontDestroyOnLoad(host);
        host.AddComponent<PerformanceMetricsService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        sampleStartTime = Time.unscaledTime;
        gcAllocatedRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Memory,
            "GC Allocated In Frame",
            1);
    }

    private void Update()
    {
        if (!collectionEnabled) return;

        sampleFrames++;
        float elapsed = Time.unscaledTime - sampleStartTime;
        if (elapsed < samplingInterval) return;

        float fps = sampleFrames / elapsed;
        long gcBytes = gcAllocatedRecorder.Valid
            ? gcAllocatedRecorder.LastValue
            : 0L;

        Snapshot = new PerformanceMetricsSnapshot(
            fps,
            fps > 0f ? 1000f / fps : 0f,
            pathRequests / elapsed,
            Average(pathTotalMs, pathSamples),
            (float)pathMaximumMs,
            Average(taskTotalMs, taskSamples),
            (float)taskMaximumMs,
            TaskManager.Instance != null ? TaskManager.Instance.PendingTaskCount : 0,
            activeResourceItems,
            activeDuplicants,
            brainTicks / elapsed,
            needsTicks / elapsed,
            fullNavRegenerations,
            partialNavRegenerations,
            navCellsRegenerated,
            globalObjectSearches,
            gcBytes,
            lastSaveMs,
            lastLoadMs,
            Average(liquidVisualizerTotalMs, liquidVisualizerSamples),
            (float)liquidVisualizerMaximumMs,
            liquidCellsProcessed,
            liquidDirtyCellsPending,
            liquidDepthColumnsPending,
            Average(liquidSimulationTotalMs, liquidSimulationSamples),
            (float)liquidSimulationMaximumMs,
            liquidSimulationSamples / elapsed,
            liquidSimulationCells / elapsed,
            liquidBufferResizes,
            lastSaveMenuMetadataMs, lastSaveReadFileMs,
            lastSaveDeserializeMs, lastSaveCaptureMs,
            lastSaveSerializeMs, lastSaveWriteMs,
            lastLoadClearWorldMs, lastLoadRestoreGridMs,
            lastLoadRestoreEntitiesMs, lastLoadPostRestoreMs,
            lastWorldGenerationMs, lastTilemapFullRefreshMs,
            lastFogFullRefreshMs);

        sampleFrames = pathRequests = pathSamples = taskSamples = 0;
        brainTicks = needsTicks = 0;
        fullNavRegenerations = partialNavRegenerations = 0;
        navCellsRegenerated = globalObjectSearches = 0;
        liquidVisualizerSamples = liquidCellsProcessed = 0;
        liquidSimulationSamples = liquidBufferResizes = 0;
        liquidSimulationCells = 0L;
        pathTotalMs = pathMaximumMs = taskTotalMs = taskMaximumMs = 0d;
        liquidVisualizerTotalMs = liquidVisualizerMaximumMs = 0d;
        liquidSimulationTotalMs = liquidSimulationMaximumMs = 0d;
        sampleStartTime = Time.unscaledTime;
    }

    private void OnDestroy()
    {
        gcAllocatedRecorder.Dispose();
        if (Instance == this) Instance = null;
    }

    private static float Average(double total, int count)
    {
        return count > 0 ? (float)(total / count) : 0f;
    }

    private void AddDuration(PerformanceMetric metric, double milliseconds)
    {
        switch (metric)
        {
            case PerformanceMetric.Pathfinding:
                pathSamples++;
                pathTotalMs += milliseconds;
                pathMaximumMs = Math.Max(pathMaximumMs, milliseconds);
                break;
            case PerformanceMetric.TaskSelection:
                taskSamples++;
                taskTotalMs += milliseconds;
                taskMaximumMs = Math.Max(taskMaximumMs, milliseconds);
                break;
            case PerformanceMetric.LiquidVisualization:
                liquidVisualizerSamples++;
                liquidVisualizerTotalMs += milliseconds;
                liquidVisualizerMaximumMs = Math.Max(
                    liquidVisualizerMaximumMs,
                    milliseconds);
                break;
            case PerformanceMetric.LiquidSimulation:
                liquidSimulationSamples++;
                liquidSimulationTotalMs += milliseconds;
                liquidSimulationMaximumMs = Math.Max(
                    liquidSimulationMaximumMs,
                    milliseconds);
                break;
            case PerformanceMetric.Save:
                lastSaveMs = (float)milliseconds;
                break;
            case PerformanceMetric.Load:
                lastLoadMs = (float)milliseconds;
                break;
            case PerformanceMetric.SaveMenuMetadata:
                lastSaveMenuMetadataMs = Mathf.Max(
                    lastSaveMenuMetadataMs,
                    (float)milliseconds);
                break;
            case PerformanceMetric.SaveReadFile: lastSaveReadFileMs = (float)milliseconds; break;
            case PerformanceMetric.SaveDeserialize: lastSaveDeserializeMs = (float)milliseconds; break;
            case PerformanceMetric.SaveCapture: lastSaveCaptureMs = (float)milliseconds; break;
            case PerformanceMetric.SaveSerialize: lastSaveSerializeMs = (float)milliseconds; break;
            case PerformanceMetric.SaveWrite: lastSaveWriteMs = (float)milliseconds; break;
            case PerformanceMetric.LoadClearWorld: lastLoadClearWorldMs = (float)milliseconds; break;
            case PerformanceMetric.LoadRestoreGrid: lastLoadRestoreGridMs = (float)milliseconds; break;
            case PerformanceMetric.LoadRestoreEntities: lastLoadRestoreEntitiesMs = (float)milliseconds; break;
            case PerformanceMetric.LoadPostRestore: lastLoadPostRestoreMs = (float)milliseconds; break;
            case PerformanceMetric.WorldGeneration: lastWorldGenerationMs = (float)milliseconds; break;
            case PerformanceMetric.TilemapFullRefresh: lastTilemapFullRefreshMs = (float)milliseconds; break;
            case PerformanceMetric.FogFullRefresh: lastFogFullRefreshMs = (float)milliseconds; break;
        }
    }
#endif

    public static long BeginSample()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return Instance != null && Instance.collectionEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
#else
        return 0L;
#endif
    }

    public static void EndSample(PerformanceMetric metric, long startedAt)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance == null || !Instance.collectionEnabled || startedAt == 0L) return;
        double milliseconds = (Stopwatch.GetTimestamp() - startedAt)
            * 1000d / Stopwatch.Frequency;
        Instance.AddDuration(metric, milliseconds);
#endif
    }

    public static void RecordPathRequest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null && Instance.collectionEnabled) Instance.pathRequests++;
#endif
    }

    public static void RecordLiquidVisualizerWork(
        long startedAt,
        int processedCells,
        int pendingCells,
        int pendingDepthColumns)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance == null || !Instance.collectionEnabled)
        {
            return;
        }

        Instance.liquidCellsProcessed += Mathf.Max(0, processedCells);
        Instance.liquidDirtyCellsPending = Mathf.Max(0, pendingCells);
        Instance.liquidDepthColumnsPending = Mathf.Max(
            0,
            pendingDepthColumns);
        EndSample(PerformanceMetric.LiquidVisualization, startedAt);
#endif
    }

    public static void RecordLiquidSimulationWork(
        long startedAt,
        int processedCells)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance == null || !Instance.collectionEnabled)
        {
            return;
        }

        Instance.liquidSimulationCells += Mathf.Max(0, processedCells);
        EndSample(PerformanceMetric.LiquidSimulation, startedAt);
#endif
    }

    public static void RecordLiquidBufferResize()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null && Instance.collectionEnabled)
        {
            Instance.liquidBufferResizes++;
        }
#endif
    }

    public static void RecordNavRegeneration(bool full, int cells)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance == null || !Instance.collectionEnabled) return;
        if (full) Instance.fullNavRegenerations++;
        else Instance.partialNavRegenerations++;
        Instance.navCellsRegenerated += Mathf.Max(0, cells);
#endif
    }

    public static void RecordBrainTick()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null && Instance.collectionEnabled) Instance.brainTicks++;
#endif
    }

    public static void RecordNeedsTick()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null && Instance.collectionEnabled) Instance.needsTicks++;
#endif
    }

    public static void RecordGlobalObjectSearch()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null && Instance.collectionEnabled) Instance.globalObjectSearches++;
#endif
    }

    public static void ChangeActiveResourceItems(int delta)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null) Instance.activeResourceItems = Mathf.Max(0, Instance.activeResourceItems + delta);
#endif
    }

    public static void ChangeActiveDuplicants(int delta)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Instance != null) Instance.activeDuplicants = Mathf.Max(0, Instance.activeDuplicants + delta);
#endif
    }
}
