using UnityEngine;
using UnityEngine.InputSystem;

public class DevModeManager : Singleton<DevModeManager>
{
    private enum DevPanelTab
    {
        Tools,
        Duplicant,
        Performance,
        World
    }

    public enum DevTool
    {
        None,
        PaintSolid,
        PaintEmpty,
        PaintLadder,
        SpawnChest,
        SpawnResource,
        SpawnLiquid,
        SpawnPreparedFood,
        SpawnDuplicant,
        SpawnFoodPlant,
        TeleportDuplicant
    }

    [Header("Configurações do Sandbox")]
    public bool isDevModeActive;
    public DevTool currentTool = DevTool.None;

    [Header("Referências")]
    public GameObject duplicantPrefab;

    [SerializeField]
    private DuplicantSpawnService duplicantSpawnService;

    [Header("Líquido de teste")]
    [SerializeField, Min(0.01f)]
    private float devLiquidAmount = 1f;

    private DuplicantController selectedDuplicant;
    private BoxSelectionHandler selectionHandler;
    private PlayerInput playerInput;
    private bool selectionHandlerWasEnabled;
    private bool playerInputWasEnabled;
    private DevPanelTab selectedPanelTab = DevPanelTab.Tools;
    private Vector2 toolsScrollPosition;
    private Vector2 diagnosticScrollPosition;
    private Vector2 performanceScrollPosition;
    private Vector2 worldScrollPosition;

    private Rect MainWindowRect => new Rect(
        10f,
        10f,
        620f,
        Mathf.Max(300f, Screen.height - 20f));

    private void Update()
    {
        bool gameplayKeyboardAllowed = InputContextService.Instance == null
            || InputContextService.Instance.IsGameplayKeyboardAllowed;

        if (gameplayKeyboardAllowed
            && Keyboard.current != null
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
            selectionHandler =
                FindFirstObjectByType<BoxSelectionHandler>();

            playerInput =
                FindFirstObjectByType<PlayerInput>();

            if (selectionHandler != null)
            {
                selectionHandlerWasEnabled =
                    selectionHandler.enabled;

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
                selectionHandler.enabled =
                    selectionHandlerWasEnabled;
            }

            if (playerInput != null)
            {
                playerInput.enabled = playerInputWasEnabled;
            }

            currentTool = DevTool.None;
        }

        Debug.Log(
            $"[DevMode] {(active ? "ATIVADO" : "DESATIVADO")}");
    }

    private void HandleInput()
    {
        if (Mouse.current == null || Camera.main == null)
        {
            return;
        }

        Vector2 mouseScreenPosition =
            Mouse.current.position.ReadValue();

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
            GridManager.Instance.WorldToGridPosition(
                mouseWorldPosition);

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

    private bool IsPointerOverDevWindow(
        Vector2 inputScreenPosition)
    {
        Vector2 guiPosition = new Vector2(
            inputScreenPosition.x,
            Screen.height - inputScreenPosition.y);

        return MainWindowRect.Contains(guiPosition);
    }

    private void ApplyTool(
        Vector2Int gridPosition,
        Vector3 worldPosition)
    {
        bool isFirstClick =
            Mouse.current.leftButton.wasPressedThisFrame;

        switch (currentTool)
        {
            case DevTool.PaintSolid:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Solid);
                break;

            case DevTool.PaintEmpty:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Empty);
                break;

            case DevTool.PaintLadder:
                GridManager.Instance.SetTileType(
                    gridPosition.x,
                    gridPosition.y,
                    TileType.Ladder);
                break;

            case DevTool.SpawnChest:
                if (isFirstClick)
                {
                    GridManager.Instance.SetTileType(
                        gridPosition.x,
                        gridPosition.y,
                        TileType.Chest);
                }
                break;

            case DevTool.SpawnResource:
                if (isFirstClick)
                {
                    ItemSpawner.Instance?.SpawnResource(
                        ResourceType.Copper,
                        worldPosition,
                        5);
                }
                break;

            case DevTool.SpawnLiquid:
                if (isFirstClick)
                {
                    LiquidManager.Instance?.AddLiquid(
                        gridPosition.x,
                        gridPosition.y,
                        devLiquidAmount);
                }
                break;

            case DevTool.SpawnPreparedFood:
                if (isFirstClick)
                {
                    ItemSpawner.Instance?.SpawnResource(
                        ResourceType.PreparedMeal,
                        worldPosition,
                        5);
                }
                break;

            case DevTool.SpawnDuplicant:
                if (isFirstClick)
                {
                    DuplicantSpawnService spawnService =
                        GetDuplicantSpawnService();

                    if (spawnService != null)
                    {
                        spawnService.SpawnRandomGroup(
                            gridPosition,
                            1);
                    }
                    else
                    {
                        Debug.LogError(
                            "[DevMode] DuplicantSpawnService não encontrado.");
                    }
                }
                break;

            case DevTool.SpawnFoodPlant:
                if (isFirstClick)
                {
                    FloraManager.Instance?.SpawnDevFlora(gridPosition);
                }
                break;

            case DevTool.TeleportDuplicant:
                if (isFirstClick && selectedDuplicant != null)
                {
                    selectedDuplicant.RecoverTo(
                        gridPosition,
                        TaskInterruptionOrigin.DevTeleport);
                }
                break;
        }
    }

    private DuplicantSpawnService GetDuplicantSpawnService()
    {
        if (duplicantSpawnService == null)
        {
            duplicantSpawnService =
                FindFirstObjectByType<DuplicantSpawnService>();
        }

        return duplicantSpawnService;
    }

    private void SelectDuplicantAt(Vector3 worldPosition)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            worldPosition,
            0.8f);

        foreach (Collider2D hit in hits)
        {
            DuplicantController duplicant =
                hit.GetComponentInParent<DuplicantController>();

            if (duplicant == null)
            {
                continue;
            }

            selectedDuplicant = duplicant;

            Debug.Log(
                $"[DevMode] Selecionado: {duplicant.name}");

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

        DrawMainWindow();
    }

    private void DrawMainWindow()
    {
        GUILayout.BeginArea(
            MainWindowRect,
            "MODO CRIATIVO (F1)",
            GUI.skin.window);

        GUILayout.BeginHorizontal();
        DrawPanelTabButton(DevPanelTab.Tools, "Ferramentas");
        DrawPanelTabButton(DevPanelTab.Duplicant, "Personagem");
        DrawPanelTabButton(DevPanelTab.Performance, "Performance");
        DrawPanelTabButton(DevPanelTab.World, "Mundo");
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        Vector2 scrollPosition = GetSelectedScrollPosition();
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        switch (selectedPanelTab)
        {
            case DevPanelTab.Tools:
                DrawToolsContent();
                break;

            case DevPanelTab.Duplicant:
                DrawDiagnosticsContent();
                break;

            case DevPanelTab.Performance:
                DrawPerformanceContent();
                break;

            case DevPanelTab.World:
                DrawWorldContent();
                break;
        }

        GUILayout.EndScrollView();
        SetSelectedScrollPosition(scrollPosition);

        GUILayout.EndArea();
    }

    private void DrawPanelTabButton(DevPanelTab tab, string label)
    {
        bool wasEnabled = GUI.enabled;
        GUI.enabled = selectedPanelTab != tab;

        if (GUILayout.Button(label))
        {
            selectedPanelTab = tab;
        }

        GUI.enabled = wasEnabled;
    }

    private Vector2 GetSelectedScrollPosition()
    {
        return selectedPanelTab switch
        {
            DevPanelTab.Tools => toolsScrollPosition,
            DevPanelTab.Duplicant => diagnosticScrollPosition,
            DevPanelTab.Performance => performanceScrollPosition,
            DevPanelTab.World => worldScrollPosition,
            _ => Vector2.zero
        };
    }

    private void SetSelectedScrollPosition(Vector2 scrollPosition)
    {
        switch (selectedPanelTab)
        {
            case DevPanelTab.Tools:
                toolsScrollPosition = scrollPosition;
                break;

            case DevPanelTab.Duplicant:
                diagnosticScrollPosition = scrollPosition;
                break;

            case DevPanelTab.Performance:
                performanceScrollPosition = scrollPosition;
                break;

            case DevPanelTab.World:
                worldScrollPosition = scrollPosition;
                break;
        }
    }

    private void DrawToolsContent()
    {
        DrawToolToggle(DevTool.None, "Nenhuma / Jogar");
        DrawToolToggle(DevTool.PaintSolid, "Desenhar bloco sólido");
        DrawToolToggle(DevTool.PaintEmpty, "Apagar bloco");
        DrawToolToggle(DevTool.PaintLadder, "Colocar escada");

        GUILayout.Space(5f);

        DrawToolToggle(DevTool.SpawnChest, "Criar baú");
        DrawToolToggle(DevTool.SpawnResource, "Criar 5 Cobres");
        DrawToolToggle(DevTool.SpawnLiquid, "Criar líquido");
        DrawToolToggle(
            DevTool.SpawnPreparedFood,
            "Criar 5 refeições");

        GUILayout.Space(5f);

        DrawToolToggle(DevTool.SpawnDuplicant, "Criar duplicant");
        DrawToolToggle(
            DevTool.SpawnFoodPlant,
            "Criar planta comestível");

        DrawToolToggle(
            DevTool.TeleportDuplicant,
            "Teletransportar selecionado");

        GUILayout.Space(10f);
        GUILayout.Label("Botão direito: selecionar duplicant");
    }

    private void DrawToolToggle(DevTool tool, string label)
    {
        if (GUILayout.Toggle(currentTool == tool, label))
        {
            currentTool = tool;
        }
    }

    private void DrawPerformanceContent()
    {
        PerformanceMetricsService metrics =
            PerformanceMetricsService.Instance;

        if (metrics == null)
        {
            GUILayout.Label("Coletor indisponível neste build.");
            return;
        }

        PerformanceMetricsSnapshot snapshot = metrics.Snapshot;

        GUILayout.Label(
            $"FPS / frame: {snapshot.FramesPerSecond:F1} / {snapshot.FrameTimeMs:F2} ms");

        GUILayout.Label(
    $"GC/frame: {snapshot.GcAllocatedBytesPerFrame:N0} bytes");

        GUILayout.Space(4f);

        GUILayout.Label(
            $"Paths/s: {snapshot.PathRequestsPerSecond:F1}");

        GUILayout.Label(
            $"Path avg/max: {snapshot.PathfindingAverageMs:F3} / {snapshot.PathfindingMaximumMs:F3} ms");

        GUILayout.Label(
            $"Task select avg/max: {snapshot.TaskSelectionAverageMs:F3} / {snapshot.TaskSelectionMaximumMs:F3} ms");

        GUILayout.Label(
            $"Tasks pendentes: {snapshot.PendingTasks}");

        GUILayout.Space(4f);

        GUILayout.Label(
            $"Duplicants / itens: {snapshot.ActiveDuplicants} / {snapshot.ActiveResourceItems}");

        GUILayout.Label(
            $"Ticks IA/s: {snapshot.BrainTicksPerSecond:F1}");

        GUILayout.Label(
            $"Ticks necessidades/s: {snapshot.NeedsTicksPerSecond:F1}");

        GUILayout.Space(4f);

        GUILayout.Label(
            $"Nav full/partial: {snapshot.FullNavRegenerations} / {snapshot.PartialNavRegenerations}");

        GUILayout.Label(
            $"Células Nav: {snapshot.NavCellsRegenerated:N0}");

        GUILayout.Label(
            $"Nav partial avg/max: {snapshot.NavPartialAverageMs:F3} / {snapshot.NavPartialMaximumMs:F3} ms");

        GUILayout.Label(
            $"Nav pending/forced: {snapshot.NavRegionsPendingPeak} / {snapshot.NavForcedFlushes}");

        GUILayout.Label(
            $"Buscas globais: {snapshot.GlobalObjectSearches}");

        GUILayout.Label(
            $"Save / Load: {snapshot.LastSaveMs:F1} / {snapshot.LastLoadMs:F1} ms");

        GUILayout.Label(
            $"Menu metadata: {snapshot.LastSaveMenuMetadataMs:F1} ms");

        GUILayout.Label(
            $"Read / JSON load: {snapshot.LastSaveReadFileMs:F1} / {snapshot.LastSaveDeserializeMs:F1} ms");

        GUILayout.Label(
            $"Save capture/json/write: {snapshot.LastSaveCaptureMs:F1} / {snapshot.LastSaveSerializeMs:F1} / {snapshot.LastSaveWriteMs:F1} ms");

        GUILayout.Label(
            $"Load clear/grid/entities/post: {snapshot.LastLoadClearWorldMs:F1} / {snapshot.LastLoadRestoreGridMs:F1} / {snapshot.LastLoadRestoreEntitiesMs:F1} / {snapshot.LastLoadPostRestoreMs:F1} ms");

        GUILayout.Label(
            $"World gen: {snapshot.LastWorldGenerationMs:F1} ms");

        GUILayout.Label(
            $"Tilemap / Fog full: {snapshot.LastTilemapFullRefreshMs:F1} / {snapshot.LastFogFullRefreshMs:F1} ms");

        GUILayout.Label(
            $"Tilemap regional avg/max: {snapshot.TilemapRegionalAverageMs:F3} / {snapshot.TilemapRegionalMaximumMs:F3} ms");

        GUILayout.Label(
            $"Tilemap cells/pending: {snapshot.TilemapRegionalCellsProcessed:N0} / {snapshot.TilemapRegionalCellsPendingPeak:N0} / {snapshot.TilemapRegionalRegionsPendingPeak:N0} reg.");

        GUILayout.Label(
            $"Fog regional avg/max: {snapshot.FogRegionalAverageMs:F3} / {snapshot.FogRegionalMaximumMs:F3} ms");

        GUILayout.Label(
            $"Fog cells/pending: {snapshot.FogRegionalCellsProcessed:N0} / {snapshot.FogRegionalCellsPendingPeak:N0} / {snapshot.FogRegionalRegionsPendingPeak:N0} reg.");

        GUILayout.Space(4f);

        GUILayout.Label(
            $"Liquid avg/max: {snapshot.LiquidVisualizerAverageMs:F3} / {snapshot.LiquidVisualizerMaximumMs:F3} ms");

        GUILayout.Label(
            $"Liquid cells: {snapshot.LiquidCellsProcessed:N0}");

        GUILayout.Label(
            $"Liquid pending: {snapshot.LiquidDirtyCellsPending:N0} cells / {snapshot.LiquidDepthColumnsPending:N0} colunas");

        GUILayout.Space(4f);

        GUILayout.Label(
            $"Liquid sim avg/max: {snapshot.LiquidSimulationAverageMs:F3} / {snapshot.LiquidSimulationMaximumMs:F3} ms");

        GUILayout.Label(
            $"Liquid sim ticks/s: {snapshot.LiquidSimulationTicksPerSecond:F1}");

        GUILayout.Label(
            $"Liquid sim cells/s: {snapshot.LiquidSimulationCellsPerSecond:N0}");

        GUILayout.Label(
            $"Liquid buffer resizes: {snapshot.LiquidBufferResizes}");
    }

    private void DrawDiagnosticsContent()
    {
        if (selectedDuplicant == null)
        {
            GUILayout.Label(
                "Clique com o botão direito em um duplicant para inspecionar.");
            return;
        }

        DrawSelectedDuplicantDiagnostics();
    }

    private void DrawWorldContent()
    {
        GridManager grid = GridManager.Instance;

        GUILayout.Label("MUNDO");

        if (grid == null)
        {
            GUILayout.Label("GridManager não encontrado.");
            return;
        }

        GUILayout.Label($"Tamanho: {grid.width}x{grid.height}");
        GUILayout.Label($"Grid pronto: {(grid.IsGridReady ? "Sim" : "Não")}");

        LiquidManager liquidManager = LiquidManager.Instance;
        GUILayout.Label(
            liquidManager != null
                ? $"Regiões líquidas ativas: {liquidManager.PendingActiveRegionCount}"
                : "LiquidManager não encontrado.");

        GUILayout.Space(8f);
        GUILayout.Label(
            "Ferramentas de geração, biomas e iluminação poderão ser adicionadas aqui.");
    }

    private void DrawSelectedDuplicantDiagnostics()
    {
        DuplicantTaskRunner runner = selectedDuplicant.TaskRunner;

        DuplicantInventory inventory =
            selectedDuplicant.GetComponent<DuplicantInventory>();

        DuplicantVitals vitals =
            selectedDuplicant.GetComponent<DuplicantVitals>();

        DuplicantStatusEffects statusEffects =
            selectedDuplicant.GetComponent<DuplicantStatusEffects>();

        DuplicantBrain brain = selectedDuplicant.Brain;
        Task task = selectedDuplicant.currentTask;

        GUILayout.Label($"Nome: {selectedDuplicant.name}");
        GUILayout.Label($"Estado: {selectedDuplicant.currentState}");
        GUILayout.Label($"Grid: {selectedDuplicant.gridPosition}");
        GUILayout.Label(
            $"World: {selectedDuplicant.transform.position:F2}");

        if (TaskManager.Instance != null)
        {
            GUILayout.Label(
                $"Tarefas: {TaskManager.Instance.PendingTaskCount} pendentes / {TaskManager.Instance.AssignedTaskCount} atribuídas");
        }

        GUILayout.Space(8f);
        GUILayout.Label("TAREFA");

        GUILayout.Label(
            $"Tipo: {(task != null ? task.type.ToString() : "Nenhuma")}");

        GUILayout.Label(
            $"Alvo: {(task != null ? task.gridPosition.ToString() : "-")}");

        GUILayout.Label(
            $"Prioridade: {(task != null ? task.priority.ToString() : "-")}");

        GUILayout.Label(
            $"Atribuída: {(task != null && task.isAssigned ? "Sim" : "Não")}");

        GUILayout.Space(8f);
        GUILayout.Label("PERFIL DE TRABALHO");

        GUILayout.Label(
            $"Perfil: {(selectedDuplicant.workProfile != null ? selectedDuplicant.workProfile.name : "Neutro")}");

        if (task != null)
        {
            int affinity =
                selectedDuplicant.GetWorkAffinity(task.type);

            int score = TaskManager.Instance != null
                ? TaskManager.Instance.GetTaskSelectionScore(
                    task,
                    selectedDuplicant.gridPosition,
                    selectedDuplicant.workProfile)
                : 0;

            GUILayout.Label($"Afinidade atual: {affinity:+#;-#;0}");
            GUILayout.Label($"Pontuação de escolha: {score}");
        }

        GUILayout.Space(8f);
        GUILayout.Label("INVENTÁRIO");

        GUILayout.Label(
            inventory != null && inventory.HasItem
                ? $"Carregando: {inventory.CarriedAmount}x {inventory.CarriedType}"
                : "Carregando: nada");

        GUILayout.Space(8f);
        GUILayout.Label("NECESSIDADES");

        if (vitals != null)
        {
            GUILayout.Label(
                $"Energia: {vitals.CurrentEnergy:F1}/{vitals.MaximumEnergy:F1} ({vitals.EnergyPercent:P0})");

            GUILayout.Label(
                $"Fome: {vitals.CurrentHunger:F1}/{vitals.MaximumHunger:F1} ({vitals.HungerPercent:P0})");

            GUILayout.Label($"Estado: {vitals.CurrentNeedState}");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Zerar energia"))
            {
                vitals.SetEnergyPercent(0f);
            }

            if (GUILayout.Button("Energia 100%"))
            {
                vitals.SetEnergyPercent(1f);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Zerar fome"))
            {
                vitals.SetHungerPercent(0f);
            }

            if (GUILayout.Button("Fome 100%"))
            {
                vitals.SetHungerPercent(1f);
            }

            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("DuplicantVitals não encontrado.");
        }

        GUILayout.Space(8f);
        GUILayout.Label("DESCANSO");

        LifeCycleSettingsSO lifeSettings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : null;
        BedStructureBehaviour activeBed = runner != null
            ? runner.ActiveBed
            : null;

        GUILayout.Label(
            $"Na cama: {(vitals != null && vitals.IsBedResting ? "Sim" : "Não")}");
        GUILayout.Label(
            $"Retomar após load: {(brain != null && brain.MustResumeBedRestAfterLoad ? "Sim" : "Não")}");
        GUILayout.Label(
            activeBed != null
                ? $"Cama reservada: {activeBed.GridPosition}"
                : "Cama reservada: nenhuma");
        GUILayout.Label(
            $"Último resultado: {(runner != null ? runner.LastBedRestResult.ToString() : "Indisponível")}");

        if (lifeSettings != null)
        {
            GUILayout.Label(
                $"Procura / acorda: {lifeSettings.seekBedBelowEnergyPercent:P0} / {lifeSettings.wakeUpAtEnergyPercent:P0}");
            GUILayout.Label(
                $"Recuperação: {lifeSettings.bedEnergyRecoveryPerSecond:F1} energia/s");
        }

        GUILayout.Space(8f);
        GUILayout.Label("PLANO DE REFEIÇÃO");

        MealPlan mealPlan = brain != null
            ? brain.CurrentMealPlan
            : null;

        if (mealPlan != null)
        {
            bool sourceValid = mealPlan.Source != null;

            if (mealPlan.Source is Object sourceObject)
            {
                sourceValid = sourceObject != null;
            }

            GUILayout.Label(
                $"Fonte: {(sourceValid ? mealPlan.Source.SourceKind.ToString() : "Invalidada")}");

            GUILayout.Label($"Alimento: {mealPlan.FoodType}");

            GUILayout.Label(
                $"Porções reservadas: {mealPlan.ReservedPortions}");

            GUILayout.Label(
                $"Interação: {mealPlan.InteractionPosition}");

            GUILayout.Label(
                $"Meta de fome: {mealPlan.HungerTarget:F1}");

            GUILayout.Label(
                $"Viagem estimada: {mealPlan.EstimatedTravelSeconds:F1}s");

            GUILayout.Label(
                $"Fome na chegada: {mealPlan.EstimatedHungerOnArrival:F1}");
        }
        else if (brain != null)
        {
            GUILayout.Label("Nenhum plano ativo.");
            GUILayout.Label(
                $"Último motivo: {brain.LastFoodFailureReason}");

            GUILayout.Label(
                $"Detalhes: {brain.LastFoodFailureDetails}");
        }

        GUILayout.Space(8f);
        GUILayout.Label("EFEITOS");

        if (statusEffects != null
            && statusEffects.HasRawFoodDiscomfort)
        {
            GUILayout.Label(
                $"Desconforto alimentar: {statusEffects.RawFoodDiscomfortRemaining:F1}s");

            GUILayout.Label(
                $"Penalidade de trabalho: {statusEffects.WorkPenaltyPercent:P0}");
        }
        else
        {
            GUILayout.Label("Nenhum efeito ativo.");
        }

        if (statusEffects != null
            && GUILayout.Button("Limpar efeitos"))
        {
            statusEffects.ClearAll();
        }

        if (runner != null)
        {
            DrawRunnerDiagnostics(runner);
        }

        GUILayout.Space(10f);

        if (GUILayout.Button("Cancelar tarefa atual"))
        {
            selectedDuplicant.CancelCurrentTaskExecution(
                TaskInterruptionOrigin.ManualCancellation);
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
            $"Destino: {(runner.DiagnosticDestination.HasValue ? runner.DiagnosticDestination.Value.ToString() : "-")}");

        GUILayout.Label(
            $"Passo: {runner.DiagnosticPathIndex}/{runner.DiagnosticPathLength}");

        GUILayout.Label(
            $"Replans: {runner.DiagnosticReplanCount}/{runner.DiagnosticMaxReplans}");

        GUILayout.Space(8f);
        GUILayout.Label("RESERVAS");

        GUILayout.Label(
            runner.HasStorageReservation
                ? $"Baú: {runner.ReservedStorageAmount}x {runner.ReservedStorageType} em {runner.ReservedStoragePosition}"
                : "Baú: nenhuma");

        GUILayout.Label(
            runner.HasResourceReservation
                ? $"Recurso: {runner.ReservedResourceAmount}x {runner.ReservedResourceType}"
                : "Recurso: nenhuma");

        GUILayout.Label(
            runner.HasBlueprintReservation
                ? $"Blueprints: {runner.ReservedBlueprintCount} destinos / {runner.ReservedDeliveryAmount} itens"
                : "Blueprint: nenhuma");

        GUILayout.Space(8f);
        GUILayout.Label("ÚLTIMA FALHA");

        Color previousColor = GUI.color;

        GUI.color = runner.LastFailureReason == TaskFailureReason.None
            ? Color.green
            : new Color(1f, 0.65f, 0.25f);

        GUILayout.Label($"Motivo: {runner.LastFailureReason}");

        if (runner.LastFailureReason
            == TaskFailureReason.Interrupted)
        {
            GUILayout.Label(
                $"Origem: {runner.LastInterruptionOrigin}");
        }

        GUILayout.Label($"Detalhes: {runner.LastFailureDetails}");

        if (runner.LastFailureTime >= 0f)
        {
            GUILayout.Label(
                $"Há: {Mathf.Max(0f, Time.time - runner.LastFailureTime):F1}s");
        }

        GUI.color = previousColor;
    }
}
