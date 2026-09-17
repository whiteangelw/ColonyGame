using UnityEngine;
using UnityEngine.InputSystem;

public enum InputMode
{
    Dig,
    Build,
    Dismantle,
    Haul,
    Harvest,
    Cancel
}

[RequireComponent(typeof(InputContextService))]
public class PlayerInput : MonoBehaviour
{
    [Header("Modo e Seleção Atual")]
    public InputMode currentMode { get; private set; } = InputMode.Dig;
    public TileType selectedBuildTile { get; private set; } = TileType.Ladder;
    public GridLayer selectedBuildLayer { get; private set; } = GridLayer.Terrain;
    public string selectedBuildDefinitionId { get; private set; } = string.Empty;

    private bool isBuildMenuOpen = false;

    private void OnEnable()
    {
        GameEvents.OnBuildTileSelected += HandleBuildTileSelected;
        GameEvents.OnBuildRecipeSelected += HandleBuildRecipeSelected;
        GameEvents.OnBuildDefinitionSelected += HandleBuildDefinitionSelected;
    }

    private void OnDisable()
    {
        GameEvents.OnBuildTileSelected -= HandleBuildTileSelected;
        GameEvents.OnBuildRecipeSelected -= HandleBuildRecipeSelected;
        GameEvents.OnBuildDefinitionSelected -= HandleBuildDefinitionSelected;
    }

    private void Update()
    {
        HandleHotkeys();
    }

    private void HandleHotkeys()
    {
        if (InputContextService.Instance != null
            && !InputContextService.Instance.IsGameplayKeyboardAllowed)
        {
            return;
        }

        if (Keyboard.current == null)
        {
            return;
        }

        // 1. Processa atalhos de Prioridade (1 a 9)
        int pressedPriority = GetPressedPriorityKey();
        if (pressedPriority != -1)
        {
            ApplyPriorityToCurrentMode(pressedPriority);
        }

        // 2. Processa atalhos de Modos/Ferramentas
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            SetInputMode(InputMode.Dig);
            CloseBuildMenu();
        }

        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            ToggleBuildMenu();
        }

        if (Keyboard.current.gKey.wasPressedThisFrame)
        {
            SetInputMode(InputMode.Dismantle);
            CloseBuildMenu();
        }

        if (Keyboard.current.zKey.wasPressedThisFrame)
        {
            SetInputMode(InputMode.Haul);
            CloseBuildMenu();
        }

        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            SetInputMode(InputMode.Harvest);
            CloseBuildMenu();
        }


        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            SetInputMode(InputMode.Cancel);
            CloseBuildMenu();
        }
    }

    private int GetPressedPriorityKey()
    {
        if (Keyboard.current.digit1Key.wasPressedThisFrame) return 1;
        if (Keyboard.current.digit2Key.wasPressedThisFrame) return 2;
        if (Keyboard.current.digit3Key.wasPressedThisFrame) return 3;
        if (Keyboard.current.digit4Key.wasPressedThisFrame) return 4;
        if (Keyboard.current.digit5Key.wasPressedThisFrame) return 5;
        if (Keyboard.current.digit6Key.wasPressedThisFrame) return 6;
        if (Keyboard.current.digit7Key.wasPressedThisFrame) return 7;
        if (Keyboard.current.digit8Key.wasPressedThisFrame) return 8;
        if (Keyboard.current.digit9Key.wasPressedThisFrame) return 9;

        return -1;
    }

    private void ApplyPriorityToCurrentMode(int priority)
    {
        if (PriorityManager.Instance == null) return;

        switch (currentMode)
        {
            case InputMode.Dig:
                PriorityManager.Instance.SetCategoryPriority(TaskType.Dig, priority);
                break;
            case InputMode.Build:
                PriorityManager.Instance.SetCategoryPriority(TaskType.BuildTile, priority);
                break;
            case InputMode.Haul:
                PriorityManager.Instance.SetCategoryPriority(TaskType.HaulResource, priority);
                break;
            case InputMode.Harvest:
                PriorityManager.Instance.SetCategoryPriority(TaskType.Harvest, priority);
                break;
        }
    }

    public void ToggleBuildMenu()
    {
        isBuildMenuOpen = !isBuildMenuOpen;
        GameEvents.TriggerToggleBuildMenuRequested(isBuildMenuOpen);

        if (isBuildMenuOpen)
        {
            SetInputMode(InputMode.Build);
        }
    }

    public void CloseBuildMenu()
    {
        if (isBuildMenuOpen)
        {
            isBuildMenuOpen = false;
            GameEvents.TriggerToggleBuildMenuRequested(false);
        }
    }

    public void SetInputMode(InputMode mode)
    {
        currentMode = mode;
        GameEvents.TriggerInputModeChanged(currentMode);
        Debug.Log($"[PlayerInput] Modo de Ação alterado para: {currentMode}");
    }

    private void HandleBuildTileSelected(TileType tileType)
    {
        selectedBuildDefinitionId = string.Empty;
        selectedBuildTile = tileType;
        selectedBuildLayer = GridLayer.Terrain;
        SetInputMode(InputMode.Build);
        Debug.Log($"[PlayerInput] Selecionado para Construção: {selectedBuildTile}");
    }


    private void HandleBuildRecipeSelected(TileType tileType, GridLayer layer)
    {
        selectedBuildDefinitionId = string.Empty;
        selectedBuildTile = tileType;
        selectedBuildLayer = layer;
        SetInputMode(InputMode.Build);
        Debug.Log($"[PlayerInput] Construção: {tileType} na camada {layer}");
    }

    private void HandleBuildDefinitionSelected(
        string definitionId,
        TileType tileType,
        GridLayer layer)
    {
        selectedBuildDefinitionId = definitionId?.Trim() ?? string.Empty;
        selectedBuildTile = tileType;
        selectedBuildLayer = layer;
        SetInputMode(InputMode.Build);
        Debug.Log(
            $"[PlayerInput] Definição: {selectedBuildDefinitionId} "
            + $"({tileType}, {layer})");
    }
}
