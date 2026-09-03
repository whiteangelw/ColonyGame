using UnityEngine;
using UnityEngine.InputSystem;

public class DevModeManager : Singleton<DevModeManager>
{
    public enum DevTool
    {
        None,
        PaintSolid,
        PaintEmpty,
        PaintLadder,
        SpawnChest,
        SpawnResource,
        SpawnDuplicant,
        TeleportDuplicant
    }

    [Header("Configurações do Sandbox")]
    public bool isDevModeActive;
    public DevTool currentTool = DevTool.None;

    [Header("Referências")]
    public GameObject duplicantPrefab;

    private DuplicantController selectedDuplicant;
    private BoxSelectionHandler selectionHandler;
    private PlayerInput playerInput;
    private bool selectionHandlerWasEnabled;
    private bool playerInputWasEnabled;
    private Vector2 diagnosticScrollPosition;

    private Rect ToolsWindowRect => new Rect(10f, 10f, 250f, 410f);
    private Rect DiagnosticsWindowRect => new Rect(
        Mathf.Max(270f, Screen.width - 390f),
        10f,
        380f,
        Mathf.Max(300f, Screen.height - 20f)
    );

    private void Update()
    {
        if (Keyboard.current != null
            && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            ToggleDevMode(!isDevModeActive);
        }

        if (isDevModeActive)
        {
            HandleInput();
        }
    }

    private void OnDisable()
    {
        if (isDevModeActive)
        {
            ToggleDevMode(false);
        }
    }

    private void ToggleDevMode(bool active)
    {
        if (isDevModeActive == active)
        {
            return;
        }

        isDevModeActive = active;

        if (active)
        {
            selectionHandler = FindFirstObjectByType<BoxSelectionHandler>();
            playerInput = FindFirstObjectByType<PlayerInput>();

            if (selectionHandler != null)
            {
                selectionHandlerWasEnabled = selectionHandler.enabled;
                selectionHandler.enabled = false;
            }

            if (playerInput != null)
            {
                playerInputWasEnabled = playerInput.enabled;
                playerInput.enabled = false;
            }
        }
        else
        {
            if (selectionHandler != null)
            {
                selectionHandler.enabled = selectionHandlerWasEnabled;
            }

            if (playerInput != null)
            {
                playerInput.enabled = playerInputWasEnabled;
            }

            currentTool = DevTool.None;
        }

        Debug.Log(
            $"[DevMode] {(active ? "ATIVADO" : "DESATIVADO")}"
        );
    }

    private void HandleInput()
    {
        if (Mouse.current == null || Camera.main == null)
        {
            return;
        }

        Vector2 mouseScreenPosition = Mouse.current.position.ReadValue();

        if (IsPointerOverDevWindow(mouseScreenPosition)
            || IsPointerOverEventSystem())
        {
            return;
        }

        if (GridManager.Instance == null)
        {
            return;
        }

        Vector3 mouseWorldPosition =
            Camera.main.ScreenToWorldPoint(mouseScreenPosition);
        mouseWorldPosition.z = 0f;

        Vector2Int gridPosition =
            GridManager.Instance.WorldToGridPosition(mouseWorldPosition);

        if (Mouse.current.leftButton.isPressed)
        {
            ApplyTool(gridPosition, mouseWorldPosition);
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            SelectDuplicantAt(mouseWorldPosition);
        }
    }

    private bool IsPointerOverEventSystem()
    {
        return UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current
                .IsPointerOverGameObject();
    }

    private bool IsPointerOverDevWindow(Vector2 inputScreenPosition)
    {
        Vector2 guiPosition = new Vector2(
            inputScreenPosition.x,
            Screen.height - inputScreenPosition.y
        );

        return ToolsWindowRect.Contains(guiPosition)
            || DiagnosticsWindowRect.Contains(guiPosition);
    }

    private void ApplyTool(Vector2Int gridPosition, Vector3 worldPosition)
    {
        bool isFirstClick = Mouse.current.leftButton.wasPressedThisFrame;

        switch (currentTool)
        {
            case DevTool.PaintSolid:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Solid
                );
                break;

            case DevTool.PaintEmpty:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Empty
                );
                break;

            case DevTool.PaintLadder:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Ladder
                );
                break;

            case DevTool.SpawnChest:
                if (isFirstClick)
                {
                    GridManager.Instance.SetTileType(
                        gridPosition.x,
                        gridPosition.y,
                        TileType.Chest
                    );
                }
                break;

            case DevTool.SpawnResource:
                if (isFirstClick)
                {
                    ItemSpawner.Instance?.SpawnResource(
                        ResourceType.Stone,
                        worldPosition,
                        5
                    );
                }
                break;

            case DevTool.SpawnDuplicant:
                if (isFirstClick && duplicantPrefab != null)
                {
                    Instantiate(
                        duplicantPrefab,
                        worldPosition,
                        Quaternion.identity
                    );
                }
                break;

            case DevTool.TeleportDuplicant:
                if (isFirstClick && selectedDuplicant != null)
                {
                    // RecoverTo cancela a tarefa, libera reservas e sincroniza
                    // Transform e gridPosition antes do teleporte.
                    selectedDuplicant.RecoverTo(gridPosition);
                }
                break;
        }
    }

    private void SelectDuplicantAt(Vector3 worldPosition)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            worldPosition,
            0.8f
        );

        foreach (Collider2D hit in hits)
        {
            DuplicantController duplicant =
                hit.GetComponentInParent<DuplicantController>();

            if (duplicant == null)
            {
                continue;
            }

            selectedDuplicant = duplicant;
            Debug.Log($"[DevMode] Selecionado: {duplicant.name}");
            return;
        }

        selectedDuplicant = null;
    }

    private void OnGUI()
    {
        if (!isDevModeActive)
        {
            return;
        }

        DrawToolsWindow();
        DrawDiagnosticsWindow();
    }

    private void DrawToolsWindow()
    {
        GUILayout.BeginArea(
            ToolsWindowRect,
            "MODO CRIATIVO (F1)",
            GUI.skin.window
        );

        DrawToolToggle(DevTool.None, "Nenhuma / Jogar");
        DrawToolToggle(DevTool.PaintSolid, "Desenhar bloco sólido");
        DrawToolToggle(DevTool.PaintEmpty, "Apagar bloco");
        DrawToolToggle(DevTool.PaintLadder, "Colocar escada");

        GUILayout.Space(5f);
        DrawToolToggle(DevTool.SpawnChest, "Criar baú");
        DrawToolToggle(DevTool.SpawnResource, "Criar 5 pedras");

        GUILayout.Space(5f);
        DrawToolToggle(DevTool.SpawnDuplicant, "Criar duplicant");
        DrawToolToggle(
            DevTool.TeleportDuplicant,
            "Teletransportar selecionado"
        );

        GUILayout.Space(10f);
        GUILayout.Label("Botão direito: selecionar duplicant");

        GUILayout.EndArea();
    }

    private void DrawToolToggle(DevTool tool, string label)
    {
        if (GUILayout.Toggle(currentTool == tool, label))
        {
            currentTool = tool;
        }
    }

    private void DrawDiagnosticsWindow()
    {
        GUILayout.BeginArea(
            DiagnosticsWindowRect,
            "DIAGNÓSTICO DO DUPLICANT",
            GUI.skin.window
        );

        diagnosticScrollPosition = GUILayout.BeginScrollView(
            diagnosticScrollPosition
        );

        if (selectedDuplicant == null)
        {
            GUILayout.Label(
                "Clique com o botão direito em um duplicant para inspecionar."
            );
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        DrawSelectedDuplicantDiagnostics();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSelectedDuplicantDiagnostics()
    {
        DuplicantTaskRunner runner = selectedDuplicant.TaskRunner;
        DuplicantInventory inventory =
            selectedDuplicant.GetComponent<DuplicantInventory>();
        Task task = selectedDuplicant.currentTask;

        GUILayout.Label($"Nome: {selectedDuplicant.name}");
        GUILayout.Label($"Estado: {selectedDuplicant.currentState}");
        GUILayout.Label($"Grid: {selectedDuplicant.gridPosition}");
        GUILayout.Label($"World: {selectedDuplicant.transform.position:F2}");

        if (TaskManager.Instance != null)
        {
            GUILayout.Label(
                $"Tarefas: {TaskManager.Instance.PendingTaskCount} pendentes / {TaskManager.Instance.AssignedTaskCount} atribuídas"
            );
        }

        GUILayout.Space(8f);
        GUILayout.Label("TAREFA");
        GUILayout.Label($"Tipo: {(task != null ? task.type.ToString() : "Nenhuma")}");
        GUILayout.Label($"Alvo: {(task != null ? task.gridPosition.ToString() : "-")}");
        GUILayout.Label($"Prioridade: {(task != null ? task.priority.ToString() : "-")}");
        GUILayout.Label($"Atribuída: {(task != null && task.isAssigned ? "Sim" : "Não")}");

        GUILayout.Space(8f);
        GUILayout.Label("INVENTÁRIO");
        GUILayout.Label(
            inventory != null && inventory.HasItem
                ? $"Carregando: {inventory.CarriedAmount}x {inventory.CarriedType}"
                : "Carregando: nada"
        );

        if (runner != null)
        {
            DrawRunnerDiagnostics(runner);
        }

        GUILayout.Space(10f);

        if (GUILayout.Button("Cancelar tarefa atual"))
        {
            selectedDuplicant.CancelCurrentTaskExecution();
        }

        if (runner != null
            && GUILayout.Button("Limpar último motivo de falha"))
        {
            runner.ClearFailureDiagnostics();
        }

        if (GUILayout.Button("Recalcular navegação e tarefas"))
        {
            NavGraphGenerator.Instance?.RegenerateGraph();
            ReachabilityManager.Instance?.RecalculateAllGroups();
            StructureManager.Instance?.QueueExistingGroundItems();
        }

        if (GUILayout.Button("Limpar seleção"))
        {
            selectedDuplicant = null;
        }
    }

    private void DrawRunnerDiagnostics(DuplicantTaskRunner runner)
    {
        GUILayout.Space(8f);
        GUILayout.Label("CAMINHO");
        GUILayout.Label(
            $"Destino: {(runner.DiagnosticDestination.HasValue ? runner.DiagnosticDestination.Value.ToString() : "-")}"
        );
        GUILayout.Label(
            $"Passo: {runner.DiagnosticPathIndex}/{runner.DiagnosticPathLength}"
        );
        GUILayout.Label(
            $"Replans: {runner.DiagnosticReplanCount}/{runner.DiagnosticMaxReplans}"
        );

        GUILayout.Space(8f);
        GUILayout.Label("RESERVAS");
        GUILayout.Label(
            runner.HasStorageReservation
                ? $"Baú: {runner.ReservedStorageAmount}x {runner.ReservedStorageType} em {runner.ReservedStoragePosition}"
                : "Baú: nenhuma"
        );
        GUILayout.Label(
            runner.HasResourceReservation
                ? $"Recurso: {runner.ReservedResourceAmount}x {runner.ReservedResourceType}"
                : "Recurso: nenhuma"
        );
        GUILayout.Label(
            runner.HasBlueprintReservation
                ? $"Blueprint: {runner.ReservedDeliveryAmount}"
                : "Blueprint: nenhuma"
        );

        GUILayout.Space(8f);
        GUILayout.Label("ÚLTIMA FALHA");

        Color previousColor = GUI.color;
        GUI.color = runner.LastFailureReason == TaskFailureReason.None
            ? Color.green
            : new Color(1f, 0.65f, 0.25f);

        GUILayout.Label($"Motivo: {runner.LastFailureReason}");
        GUILayout.Label($"Detalhes: {runner.LastFailureDetails}");

        if (runner.LastFailureTime >= 0f)
        {
            GUILayout.Label(
                $"Há: {Mathf.Max(0f, Time.time - runner.LastFailureTime):F1}s"
            );
        }

        GUI.color = previousColor;
    }
}
