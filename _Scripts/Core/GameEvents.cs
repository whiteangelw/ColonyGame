using System;
using UnityEngine;

public static class GameEvents
{
    // --- EVENTOS DE RECURSOS E ESTOQUE ---
    public static event Action<ResourceType, int> OnResourceAmountChanged;
    public static void TriggerResourceAmountChanged(ResourceType type, int amount) => OnResourceAmountChanged?.Invoke(type, amount);
    public static event Action<ResourceType> OnResourceUnlocked;
    public static void TriggerResourceUnlocked(ResourceType type) => OnResourceUnlocked?.Invoke(type);

    // --- EVENTOS DE TEMPO E CICLO DIA/NOITE ---
    public static event Action<int, string, string> OnTimeUpdated;
    public static void TriggerTimeUpdated(int day, string timeString, string periodName) => OnTimeUpdated?.Invoke(day, timeString, periodName);

    // --- EVENTOS DE NÉVOA / FOG OF WAR ---
    public static event Action<int, int, FogState> OnFogStateChanged;
    public static void TriggerFogStateChanged(int x, int y, FogState newState) => OnFogStateChanged?.Invoke(x, y, newState);

    // --- EVENTOS DE MODOS E INPUT ---
    public static event Action<InputMode> OnInputModeChanged;
    public static void TriggerInputModeChanged(InputMode newMode) => OnInputModeChanged?.Invoke(newMode);

    public static event Action<TileType> OnBuildTileSelected;
    public static void TriggerBuildTileSelected(TileType tileType) => OnBuildTileSelected?.Invoke(tileType);

    public static event Action<bool> OnToggleBuildMenuRequested;
    public static void TriggerToggleBuildMenuRequested(bool shouldOpen) => OnToggleBuildMenuRequested?.Invoke(shouldOpen);

    // --- EVENTOS DIVERSOS DA COLÔNIA ---
    public static event Action<PrintingPod> OnPrintingPodBuilt;
    public static void TriggerPrintingPodBuilt(PrintingPod printingPod)
        => OnPrintingPodBuilt?.Invoke(printingPod);

    public static event Action<PrintingPod> OnPrintingPodRemoved;
    public static void TriggerPrintingPodRemoved(PrintingPod printingPod)
        => OnPrintingPodRemoved?.Invoke(printingPod);

    public static event Action<float, bool> OnPrintingPodTimerChanged;
    public static void TriggerPrintingPodTimerChanged(
        float timeRemaining,
        bool offerReady)
        => OnPrintingPodTimerChanged?.Invoke(timeRemaining, offerReady);

    public static event Action OnRewardOfferReady;
    public static void TriggerRewardOfferReady()
        => OnRewardOfferReady?.Invoke();

    public static event Action<TaskType, int> OnPriorityChanged;
    public static void TriggerPriorityChanged(TaskType type, int newPriority)
        => OnPriorityChanged?.Invoke(type, newPriority);

    public static event Action<Vector2Int> OnChestUpdated;
    public static void TriggerChestUpdated(Vector2Int chestPos) => OnChestUpdated?.Invoke(chestPos);

    // --- EVENTOS DE UI E FEEDBACK VISUAL ---
    public static event Action<string, Vector3, Color> OnFloatingTextRequested;
    public static void TriggerFloatingTextRequested(string text, Vector3 worldPosition, Color color)
        => OnFloatingTextRequested?.Invoke(text, worldPosition, color);
}
