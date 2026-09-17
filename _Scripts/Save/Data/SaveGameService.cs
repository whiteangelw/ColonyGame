using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Unity.Profiling;

[DefaultExecutionOrder(-1000)]
public class SaveGameService : MonoBehaviour
{
    private static readonly ProfilerMarker SaveMarker =
        new ProfilerMarker("Colony.SaveLoad.Save");
    private static readonly ProfilerMarker RestoreMarker =
        new ProfilerMarker("Colony.SaveLoad.RestoreWorld");
    private static readonly ProfilerMarker MenuMetadataMarker =
        new ProfilerMarker("SaveGame.MenuSlotMetadata");
    private static readonly ProfilerMarker ReadFileMarker =
        new ProfilerMarker("SaveGame.ReadFile");
    private static readonly ProfilerMarker DeserializeMarker =
        new ProfilerMarker("SaveGame.DeserializeJson");
    private static readonly ProfilerMarker CaptureMarker =
        new ProfilerMarker("SaveGame.CaptureWorld");
    private static readonly ProfilerMarker SerializeMarker =
        new ProfilerMarker("SaveGame.SerializeJson");
    private static readonly ProfilerMarker WriteMarker =
        new ProfilerMarker("SaveGame.WriteFile");
    private static readonly ProfilerMarker ClearWorldMarker =
        new ProfilerMarker("SaveGame.ClearRuntimeWorld");
    private static readonly ProfilerMarker RestoreGridMarker =
        new ProfilerMarker("SaveGame.RestoreGrid");
    private static readonly ProfilerMarker RestoreGridBatchMarker =
        new ProfilerMarker("SaveGame.RestoreGridBatch");
    private static readonly ProfilerMarker RestoreEntitiesMarker =
        new ProfilerMarker("SaveGame.RestoreEntities");
    private static readonly ProfilerMarker PostRestoreMarker =
        new ProfilerMarker("SaveGame.PostRestore");
    public static SaveGameService Instance { get; private set; }

    // Campos de camada usam zero como padrão, mantendo compatibilidade com saves v1.
    private const int MinimumSupportedSaveVersion = 1;
    private const int CurrentSaveVersion = 3;
    public const int MaxSaveSlots = 3;
    private const string SaveDirectoryName = "Saves";
    private const string LegacySaveFileName = "colony-save.json";
    private const string LegacyBackupFileName = "colony-save.backup.json";

    [Serializable]
    private sealed class SaveSlotMetadata
    {
        public int saveVersion;
        public string savedAtUtc;
        public int worldWidth;
        public int worldHeight;
    }

    private sealed class SaveBackgroundResult
    {
        public bool succeeded;
        public string error;
        public string metadataWarning;
        public double serializeMilliseconds;
        public double writeMilliseconds;
    }

    private sealed class LoadBackgroundResult
    {
        public SaveGameData data;
        public string error;
        public bool usedFallback;
        public double readMilliseconds;
        public double deserializeMilliseconds;
    }

    [Header("Referências")]
    [SerializeField] private DuplicantSpawnService duplicantSpawnService;
    [SerializeField] private CameraController cameraController;

    [Header("Inicialização")]
    [Tooltip("Carrega automaticamente um save válido antes de gerar um mundo novo.")]
    [SerializeField] private bool loadAutomaticallyOnStart = true;

    [Header("Carregamento incremental")]
    [Tooltip("Quantidade máxima de células restauradas por frame. Valores menores reduzem picos, mas aumentam o tempo total de carregamento.")]
    [SerializeField, Min(128)] private int gridRestoreTilesPerFrame = 4096;

    [Header("Diagnóstico")]
    [SerializeField] private string lastOperationMessage = "Nenhuma operação";

    [Header("Slot atual")]
    [SerializeField, Range(1, MaxSaveSlots)] private int activeSlot = 1;

    public bool IsBusy { get; private set; }
    public bool IsAutomaticLoadInProgress { get; private set; }
    public bool IsWaitingForStartupChoice { get; private set; }
    public int ActiveSlot => activeSlot;
    public string LastOperationMessage => lastOperationMessage;
    public string SavePath => GetSavePath(activeSlot);
    public bool HasSave => HasSaveInSlot(activeSlot);

    public event Action<bool, string> OnStartupSessionFinished;
    public event Action<bool, string> OnSaveFinished;
    public event Action<float, string> OnLoadProgress;

    public bool SelectSlot(int slot)
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return false;
        }

        activeSlot = Mathf.Clamp(slot, 1, MaxSaveSlots);
        lastOperationMessage = HasSave
            ? $"Slot {activeSlot} selecionado."
            : $"Slot {activeSlot} está vazio.";
        return true;
    }

    public bool HasSaveInSlot(int slot)
    {
        slot = Mathf.Clamp(slot, 1, MaxSaveSlots);

        if (File.Exists(GetSavePath(slot))
            || File.Exists(GetBackupPath(slot)))
        {
            return true;
        }

        return slot == 1
            && (File.Exists(GetLegacySavePath())
                || File.Exists(GetLegacyBackupPath()));
    }

    public string GetSlotDescription(int slot)
    {
        slot = Mathf.Clamp(slot, 1, MaxSaveSlots);
        long startedAt = PerformanceMetricsService.BeginSample();
        string description;
        using (MenuMetadataMarker.Auto())
        {
            description = BuildSlotDescription(slot);
        }
        PerformanceMetricsService.EndSample(
            PerformanceMetric.SaveMenuMetadata,
            startedAt);
        return description;
    }

    private static string BuildSlotDescription(int slot)
    {
        if (!TryResolveExistingSavePath(slot, out string savePath))
        {
            return $"Slot {slot} — Vazio";
        }

        if (TryReadSlotMetadata(slot, out SaveSlotMetadata metadata)
            && DateTime.TryParse(
                metadata.savedAtUtc,
                out DateTime metadataSavedAt))
        {
            return $"Slot {slot} — {metadataSavedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
        }

        // Compatibilidade com saves anteriores à criação do arquivo de
        // metadados. Consultar a data do arquivo não lê o snapshot 500x500.
        try
        {
            DateTime fileModifiedAt =
                File.GetLastWriteTimeUtc(savePath).ToLocalTime();
            return $"Slot {slot} — {fileModifiedAt:dd/MM/yyyy HH:mm}";
        }
        catch
        {
            return $"Slot {slot} — Colônia salva";
        }
    }

    private static bool TryResolveExistingSavePath(int slot, out string path)
    {
        string primaryPath = GetSavePath(slot);
        if (File.Exists(primaryPath))
        {
            path = primaryPath;
            return true;
        }

        string backupPath = GetBackupPath(slot);
        if (File.Exists(backupPath))
        {
            path = backupPath;
            return true;
        }

        if (slot == 1)
        {
            string legacyPath = GetLegacySavePath();
            if (File.Exists(legacyPath))
            {
                path = legacyPath;
                return true;
            }

            string legacyBackupPath = GetLegacyBackupPath();
            if (File.Exists(legacyBackupPath))
            {
                path = legacyBackupPath;
                return true;
            }
        }

        path = null;
        return false;
    }

    private static bool TryReadSlotMetadata(
        int slot,
        out SaveSlotMetadata metadata)
    {
        metadata = null;
        string path = GetMetadataPath(slot);
        if (!File.Exists(path)) return false;

        try
        {
            metadata = JsonUtility.FromJson<SaveSlotMetadata>(
                File.ReadAllText(path));
            return metadata != null
                && metadata.saveVersion == CurrentSaveVersion
                && !string.IsNullOrWhiteSpace(metadata.savedAtUtc);
        }
        catch
        {
            metadata = null;
            return false;
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool TryBeginAutomaticLoad()
    {
        if (IsWaitingForStartupChoice)
        {
            return true;
        }

        if (!loadAutomaticallyOnStart)
        {
            return false;
        }

        if (IsAutomaticLoadInProgress)
        {
            return true;
        }

        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return false;
        }

        if (!HasSave)
        {
            lastOperationMessage =
                "Nenhum save encontrado. Uma nova colônia será criada.";
            Debug.Log(
                "[SaveGameService] " + lastOperationMessage
            );
            return false;
        }

        IsAutomaticLoadInProgress = true;
        lastOperationMessage = "Carregamento automático iniciado.";
        StartCoroutine(LoadFromDiskRoutine(false, true));
        return true;
    }

    public void RequireStartupChoice()
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return;
        }

        IsWaitingForStartupChoice = true;
        lastOperationMessage = HasSave
            ? "Escolha Continuar ou Novo Jogo."
            : "Nenhum save encontrado. Inicie um Novo Jogo.";
    }

    public bool ContinueFromStartupMenu()
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return false;
        }

        if (!HasSave)
        {
            lastOperationMessage = "Nenhum save encontrado neste slot.";
            OnStartupSessionFinished?.Invoke(false, lastOperationMessage);
            return false;
        }

        IsWaitingForStartupChoice = false;
        IsAutomaticLoadInProgress = true;
        lastOperationMessage = "Carregando colônia...";
        StartCoroutine(LoadFromDiskRoutine(true, false));
        return true;
    }

    public bool StartNewGameFromStartupMenu(bool archiveExistingSave = true)
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return false;
        }

        if (archiveExistingSave && HasSave && !TryArchiveCurrentSave(out string error))
        {
            lastOperationMessage = error;
            OnStartupSessionFinished?.Invoke(false, error);
            return false;
        }

        GridManager grid = GridManager.Instance;

        if (grid == null)
        {
            lastOperationMessage = "GridManager não encontrado.";
            OnStartupSessionFinished?.Invoke(false, lastOperationMessage);
            return false;
        }

        IsWaitingForStartupChoice = false;
        grid.GenerateNewWorld();
        lastOperationMessage = "Nova colônia iniciada.";
        OnStartupSessionFinished?.Invoke(true, lastOperationMessage);
        return true;
    }

    private bool TryArchiveCurrentSave(out string error)
    {
        error = null;

        try
        {
            string archiveDirectory = Path.Combine(
                Application.persistentDataPath,
                SaveDirectoryName,
                "Archive"
            );
            Directory.CreateDirectory(archiveDirectory);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            ArchiveFileIfPresent(
                SavePath,
                Path.Combine(
                    archiveDirectory,
                    $"{stamp}-slot-{activeSlot}-colony-save.json"
                )
            );
            ArchiveFileIfPresent(
                GetBackupPath(activeSlot),
                Path.Combine(
                    archiveDirectory,
                    $"{stamp}-slot-{activeSlot}-colony-save.backup.json"
                )
            );
            ArchiveFileIfPresent(
                GetMetadataPath(activeSlot),
                Path.Combine(
                    archiveDirectory,
                    $"{stamp}-slot-{activeSlot}-metadata.json"
                )
            );

            if (activeSlot == 1)
            {
                ArchiveFileIfPresent(
                    GetLegacySavePath(),
                    Path.Combine(
                        archiveDirectory,
                        stamp + "-legacy-colony-save.json"
                    )
                );
                ArchiveFileIfPresent(
                    GetLegacyBackupPath(),
                    Path.Combine(
                        archiveDirectory,
                        stamp + "-legacy-colony-save.backup.json"
                    )
                );
            }

            return true;
        }
        catch (Exception exception)
        {
            error = "Não foi possível arquivar o save anterior: "
                + exception.Message;
            return false;
        }
    }

    private static void ArchiveFileIfPresent(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination);
        }
    }

    [ContextMenu("Save Game")]
    public void SaveGame()
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return;
        }

        StartCoroutine(SaveRoutine());
    }

    private IEnumerator SaveRoutine()
    {
        IsBusy = true;
        lastOperationMessage = "Salvando colônia...";
        long startedAt = PerformanceMetricsService.BeginSample();
        SaveGameData data;

        try
        {
            long captureStartedAt = PerformanceMetricsService.BeginSample();
            using (SaveMarker.Auto())
            using (CaptureMarker.Auto())
                data = CaptureGame();
            PerformanceMetricsService.EndSample(
                PerformanceMetric.SaveCapture,
                captureStartedAt);
        }
        catch (Exception exception)
        {
            lastOperationMessage = "Falha ao salvar: " + exception.Message;
            Debug.LogError("[SaveGameService] " + lastOperationMessage);
            IsBusy = false;
            PerformanceMetricsService.EndSample(
                PerformanceMetric.Save,
                startedAt);
            OnSaveFinished?.Invoke(false, lastOperationMessage);
            yield break;
        }

        string savePath = SavePath;
        string backupPath = GetBackupPath(activeSlot);
        string metadataPath = GetMetadataPath(activeSlot);
        string metadataJson = CreateSlotMetadataJson(data);

        System.Threading.Tasks.Task<SaveBackgroundResult> saveTask =
            System.Threading.Tasks.Task.Run(
            () => ExecuteSaveInBackground(
                data,
                savePath,
                backupPath,
                metadataPath,
                metadataJson));

        while (!saveTask.IsCompleted)
        {
            yield return null;
        }

        SaveBackgroundResult result = saveTask.Result;
        PerformanceMetricsService.RecordDuration(
            PerformanceMetric.SaveSerialize,
            result.serializeMilliseconds);
        PerformanceMetricsService.RecordDuration(
            PerformanceMetric.SaveWrite,
            result.writeMilliseconds);

        if (result.succeeded)
        {
            lastOperationMessage = "Jogo salvo com sucesso.";
            Debug.Log($"[SaveGameService] {lastOperationMessage} {savePath}");

            if (!string.IsNullOrEmpty(result.metadataWarning))
            {
                Debug.LogWarning(
                    "[SaveGameService] Save concluído, mas os metadados "
                    + "não puderam ser atualizados: "
                    + result.metadataWarning);
            }
        }
        else
        {
            lastOperationMessage = "Falha ao salvar: " + result.error;
            Debug.LogError("[SaveGameService] " + lastOperationMessage);
        }

        IsBusy = false;
        PerformanceMetricsService.EndSample(PerformanceMetric.Save, startedAt);
        OnSaveFinished?.Invoke(result.succeeded, lastOperationMessage);
    }

    private static SaveBackgroundResult ExecuteSaveInBackground(
        SaveGameData data,
        string savePath,
        string backupPath,
        string metadataPath,
        string metadataJson)
    {
        SaveBackgroundResult result = new SaveBackgroundResult();

        try
        {
            long phaseStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            string json;
            using (SerializeMarker.Auto())
                json = JsonUtility.ToJson(data, false);
            result.serializeMilliseconds = ElapsedMilliseconds(phaseStartedAt);

            phaseStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            using (WriteMarker.Auto())
            {
                WriteAtomically(savePath, backupPath, json);
                try
                {
                    WriteSlotMetadata(metadataPath, metadataJson);
                }
                catch (Exception exception)
                {
                    // O snapshot principal já foi salvo. Metadados continuam
                    // sendo auxiliares e não invalidam a operação principal.
                    result.metadataWarning = exception.Message;
                }
            }
            result.writeMilliseconds = ElapsedMilliseconds(phaseStartedAt);
            result.succeeded = true;
        }
        catch (Exception exception)
        {
            result.error = exception.Message;
        }

        return result;
    }

    private static double ElapsedMilliseconds(long startedAt)
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
            * 1000d / System.Diagnostics.Stopwatch.Frequency;
    }

    [ContextMenu("Load Game")]
    public void LoadGame()
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return;
        }

        if (!HasSave)
        {
            lastOperationMessage = "Nenhum save encontrado neste slot.";
            Debug.LogError("[SaveGameService] " + lastOperationMessage);
            return;
        }

        StartCoroutine(LoadFromDiskRoutine());
    }

    private SaveGameData CaptureGame()
    {
        GridManager grid = GridManager.Instance;

        if (grid == null || !grid.IsGridReady)
        {
            throw new InvalidOperationException("O Grid ainda não está pronto.");
        }

        SaveGameData data = new SaveGameData
        {
            saveVersion = CurrentSaveVersion,
            savedAtUtc = DateTime.UtcNow.ToString("O")
        };

        CaptureWorld(data, grid);
        CaptureStructures(data);
        CaptureBlueprints(data);
        CaptureGroundResources(data);
        CaptureDuplicants(data);
        CaptureFlora(data);
        CapturePersistentTasks(data);
        CapturePrintingPodScheduler(data);
        CaptureDayNightCycle(data);
        CaptureCamera(data);
        CaptureRecipeUnlocks(data);
        return data;
    }

    private static void CaptureWorld(SaveGameData data, GridManager grid)
    {
        data.world.width = grid.width;
        data.world.height = grid.height;
        data.world.cellSize = grid.cellSize;

        WorldGenerator generator = grid.GetComponent<WorldGenerator>();
        data.world.generationSeed = generator != null
            ? generator.CurrentSeed
            : 0f;

        int cellCount = checked(grid.width * grid.height);
        data.world.tiles.Clear();
        data.world.tileTypes = new int[cellCount];
        data.world.backWallTileTypes = new int[cellCount];
        data.world.decorationTileTypes = new int[cellCount];
        data.world.fogStates = new int[cellCount];
        data.world.liquidAmounts = new float[cellCount];
        data.world.contentCells = new List<TileContentSaveData>(256);

        for (int x = 0; x < grid.width; x++)
        {
            for (int y = 0; y < grid.height; y++)
            {
                Tile tile = grid.GetTile(x, y);

                if (tile == null)
                {
                    throw new InvalidOperationException(
                        $"Célula ausente durante o save: ({x}, {y}).");
                }

                int cellIndex = x * grid.height + y;
                data.world.tileTypes[cellIndex] = (int)tile.type;
                data.world.backWallTileTypes[cellIndex] =
                    (int)tile.backWallType;
                data.world.decorationTileTypes[cellIndex] =
                    (int)tile.decorationType;
                data.world.fogStates[cellIndex] = (int)tile.fogState;
                data.world.liquidAmounts[cellIndex] = tile.liquidAmount;

                if (HasAnyContentId(tile))
                {
                    data.world.contentCells.Add(new TileContentSaveData
                    {
                        cellIndex = cellIndex,
                        terrainContentId = tile.terrainContentId,
                        structureContentId = tile.structureContentId,
                        backWallContentId = tile.backWallContentId,
                        decorationContentId = tile.decorationContentId
                    });
                }
            }
        }
    }

    private static bool HasAnyContentId(Tile tile)
    {
        return !string.IsNullOrEmpty(tile.terrainContentId)
            || !string.IsNullOrEmpty(tile.structureContentId)
            || !string.IsNullOrEmpty(tile.backWallContentId)
            || !string.IsNullOrEmpty(tile.decorationContentId);
    }

    private static void CaptureStructures(SaveGameData data)
    {
        StructureManager manager = StructureManager.Instance;

        if (manager == null)
        {
            return;
        }

        foreach (StorageStructure storage in manager.GetRegisteredStorages())
        {
            string definitionId = GridManager.Instance?.GetContentId(
                storage.GridPosition.x,
                storage.GridPosition.y,
                GridLayer.Structure);
            BuildDefinitionSO definition =
                BuildCatalogService.Instance?.GetById(definitionId);
            StructureSaveData savedStorage = new StructureSaveData
            {
                x = storage.GridPosition.x,
                y = storage.GridPosition.y,
                tileType = (int)(definition != null
                    ? definition.TileType
                    : TileType.Chest),
                definitionId = definitionId,
                isBeingDismantled = storage.IsBeingDismantled,
                maxCapacityPerResource = storage.maxCapacityPerResource
            };

            foreach (var entry in storage.GetAllStoredItems())
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                savedStorage.storedItems.Add(new ResourceAmountSaveData
                {
                    resourceType = (int)entry.Key,
                    amount = entry.Value
                });
            }

            data.structures.Add(savedStorage);
        }

        foreach (PrintingPod printingPod in manager.GetRegisteredPrintingPods())
        {
            string definitionId = GridManager.Instance?.GetContentId(
                printingPod.GridPosition.x,
                printingPod.GridPosition.y,
                GridLayer.Structure);
            BuildDefinitionSO definition =
                BuildCatalogService.Instance?.GetById(definitionId);
            data.structures.Add(new StructureSaveData
            {
                x = printingPod.GridPosition.x,
                y = printingPod.GridPosition.y,
                tileType = (int)(definition != null
                    ? definition.TileType
                    : TileType.PrintingPod),
                definitionId = definitionId
            });
        }

        foreach (ConfiguredStructure structure in manager.GetRegisteredConfiguredStructures())
        {
            if (structure.UsesSpecializedSaveData)
            {
                continue;
            }

            BuildDefinitionSO definition =
                BuildCatalogService.Instance?.GetById(structure.DefinitionId);
            data.structures.Add(new StructureSaveData
            {
                x = structure.GridPosition.x,
                y = structure.GridPosition.y,
                tileType = (int)(definition != null
                    ? definition.TileType
                    : TileType.Structure),
                definitionId = structure.DefinitionId,
                isBeingDismantled = structure.IsBeingDismantled
            });
        }
    }

    private static void CaptureBlueprints(SaveGameData data)
    {
        ConstructionBlueprint[] blueprints =
            FindObjectsByType<ConstructionBlueprint>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (ConstructionBlueprint blueprint in blueprints)
        {
            data.blueprints.Add(new BlueprintSaveData
            {
                x = blueprint.gridPosition.x,
                y = blueprint.gridPosition.y,
                targetTileType = (int)blueprint.targetTileType,
                buildDefinitionId = blueprint.buildDefinitionId,
                buildLayer = (int)blueprint.buildLayer,
                requiredResource = (int)blueprint.requiredResource,
                requiredAmount = blueprint.requiredAmount,
                deliveredAmount = blueprint.deliveredAmount,
                totalWorkRequired = blueprint.totalWorkRequired,
                currentWorkDone = blueprint.currentWorkDone
            });
        }
    }

    private static void CaptureGroundResources(SaveGameData data)
    {
        ResourceItem[] resources = FindObjectsByType<ResourceItem>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (ResourceItem resource in resources)
        {
            if (resource == null || resource.amount <= 0)
            {
                continue;
            }

            Vector3 position = resource.transform.position;
            data.groundResources.Add(new ResourceItemSaveData
            {
                resourceType = (int)resource.type,
                amount = resource.amount,
                worldX = position.x,
                worldY = position.y,
                worldZ = position.z
            });
        }
    }

    private static void CaptureDuplicants(SaveGameData data)
    {
        DuplicantController[] duplicants =
            FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (DuplicantController duplicant in duplicants)
        {
            DuplicantInventory inventory =
                duplicant.GetComponent<DuplicantInventory>();
            DuplicantSaveIdentity identity =
                duplicant.GetComponent<DuplicantSaveIdentity>();
            DuplicantVitals vitals =
                duplicant.GetComponent<DuplicantVitals>();
            DuplicantStatusEffects statusEffects =
                duplicant.GetComponent<DuplicantStatusEffects>();

            data.duplicants.Add(new DuplicantSaveData
            {
                definitionId = identity != null
                    ? identity.DefinitionId
                    : "__fallback__",
                displayName = duplicant.gameObject.name,
                gridX = duplicant.gridPosition.x,
                gridY = duplicant.gridPosition.y,
                hasCarriedResource = inventory != null && inventory.HasItem,
                carriedResourceType = inventory != null
                    && inventory.CarriedType.HasValue
                        ? (int)inventory.CarriedType.Value
                        : 0,
                carriedAmount = inventory != null
                    ? inventory.CarriedAmount
                    : 0,
                inventoryCapacity = inventory != null
                    ? inventory.maxCapacity
                    : 0,
                hasVitals = vitals != null,
                currentEnergy = vitals != null ? vitals.CurrentEnergy : 0f,
                currentHunger = vitals != null ? vitals.CurrentHunger : 0f,
                wasRestingInBed = vitals != null && vitals.IsBedResting,
                rawFoodDiscomfortRemaining = statusEffects != null
                    ? statusEffects.RawFoodDiscomfortRemaining
                    : 0f,
                rawFoodWorkPenaltyPercent = statusEffects != null
                    ? statusEffects.WorkPenaltyPercent
                    : 0f
            });
        }
    }

    private static void CaptureFlora(SaveGameData data)
    {
        if (FloraManager.Instance == null) return;

        foreach (FloraEntity source in FloraManager.Instance.GetSnapshot())
        {
            if (source == null || source.AvailableUnits <= 0) continue;

            Vector2Int position = source.GridPosition;
            data.flora.Add(new FloraSaveData
            {
                floraId = source.FloraId,
                gridX = position.x,
                gridY = position.y,
                availablePortions = source.AvailableUnits
            });
        }
    }

    private static void CapturePersistentTasks(SaveGameData data)
    {
        if (TaskManager.Instance == null)
        {
            return;
        }

        foreach (Task task in TaskManager.Instance.GetTasksSnapshot())
        {
            if (task == null
                || (task.type != TaskType.Dig
                    && task.type != TaskType.Dismantle
                    && task.type != TaskType.Harvest))
            {
                continue;
            }

            data.persistentTasks.Add(new TaskSaveData
            {
                taskType = (int)task.type,
                gridX = task.gridPosition.x,
                gridY = task.gridPosition.y,
                buildTileType = (int)task.buildTileType,
                targetLayer = (int)task.targetLayer,
                priority = task.priority
            });
        }
    }

    private static void CapturePrintingPodScheduler(SaveGameData data)
    {
        PrintingPodScheduler scheduler = PrintingPodScheduler.Instance;

        if (scheduler == null)
        {
            return;
        }

        data.printingPodScheduler.hasState = true;
        data.printingPodScheduler.timeRemaining = scheduler.TimeRemaining;
        data.printingPodScheduler.isOfferReady = scheduler.IsOfferReady;
        data.printingPodScheduler.isTimerRunning = scheduler.IsTimerRunning;
        data.printingPodScheduler.hasGeneratedFirstOffer =
            scheduler.HasGeneratedFirstOffer;
    }

    private void CaptureCamera(SaveGameData data)
    {
        CameraController controller = GetCameraController();

        if (controller == null)
        {
            return;
        }

        Vector3 position = controller.GetCameraPosition();
        data.camera.hasState = true;
        data.camera.worldX = position.x;
        data.camera.worldY = position.y;
        data.camera.worldZ = position.z;
        data.camera.orthographicSize = controller.GetZoom();
    }

    private static void CaptureDayNightCycle(SaveGameData data)
    {
        if (DayNightCycleManager.Instance != null)
        {
            data.dayNightCycle =
                DayNightCycleManager.Instance.CaptureState();
        }
    }

    private static void CaptureRecipeUnlocks(SaveGameData data)
    {
        RecipeUnlockService service = RecipeUnlockService.Instance;
        if (service == null) return;

        data.recipeUnlocks.hasState = true;
        data.recipeUnlocks.unlockedDefinitionIds =
            service.GetUnlockedDefinitionIdsSnapshot();
        data.recipeUnlocks.unlockedLegacyTileTypes =
            service.GetUnlockedLegacyTileTypesSnapshot();
    }

    private IEnumerator LoadFromDiskRoutine(
        bool notifyStartupMenu = false,
        bool generateNewWorldOnFailure = false)
    {
        long loadStartedAt = PerformanceMetricsService.BeginSample();
        IsBusy = true;
        SaveGameRuntime.IsLoading = true;
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        string[] candidatePaths = GetLoadCandidatePaths(activeSlot);
        System.Threading.Tasks.Task<LoadBackgroundResult> readTask =
            System.Threading.Tasks.Task.Run(
                () => ReadSaveInBackground(candidatePaths));

        while (!readTask.IsCompleted)
        {
            yield return null;
        }

        LoadBackgroundResult readResult = readTask.Result;
        PerformanceMetricsService.RecordDuration(
            PerformanceMetric.SaveReadFile,
            readResult.readMilliseconds);
        PerformanceMetricsService.RecordDuration(
            PerformanceMetric.SaveDeserialize,
            readResult.deserializeMilliseconds);

        if (readResult.usedFallback)
        {
            Debug.LogWarning(
                "[SaveGameService] Save principal inválido. "
                + "Um arquivo de fallback foi carregado.");
        }

        if (readResult.data == null)
        {
            lastOperationMessage = generateNewWorldOnFailure
                ? "Nenhum save válido. Uma nova colônia será criada."
                : readResult.error;

            Debug.LogError("[SaveGameService] " + lastOperationMessage);
            SaveGameRuntime.IsLoading = false;
            Time.timeScale = previousTimeScale;
            IsAutomaticLoadInProgress = false;
            IsBusy = false;
            PerformanceMetricsService.EndSample(
                PerformanceMetric.Load,
                loadStartedAt);

            if (generateNewWorldOnFailure)
            {
                GridManager.Instance?.GenerateNewWorld();
            }

            if (notifyStartupMenu)
            {
                IsWaitingForStartupChoice = true;
                OnStartupSessionFinished?.Invoke(false, lastOperationMessage);
            }

            yield break;
        }

        SaveGameData data = readResult.data;

        Exception loadException = null;

        try
        {
            long phaseStartedAt = PerformanceMetricsService.BeginSample();
            using (ClearWorldMarker.Auto()) ClearRuntimeWorld();
            PerformanceMetricsService.EndSample(PerformanceMetric.LoadClearWorld, phaseStartedAt);
        }
        catch (Exception exception)
        {
            loadException = exception;
        }

        // Destroy é concluído no fim do frame. A restauração só começa
        // depois que as instâncias antigas realmente deixaram de existir.
        yield return null;

        if (loadException == null)
        {
            long gridPhaseStartedAt = PerformanceMetricsService.BeginSample();
            yield return RestoreGridIncrementally(
                data.world,
                exception => loadException = exception);

            if (loadException == null)
            {
                yield return RestoreWorldSystemsIncrementally(
                    exception => loadException = exception);
            }

            PerformanceMetricsService.EndSample(
                PerformanceMetric.LoadRestoreGrid,
                gridPhaseStartedAt);
        }

        if (loadException == null)
        {
            try
            {
                using (RestoreMarker.Auto())
                {
                    long phaseStartedAt = PerformanceMetricsService.BeginSample();
                    using (RestoreEntitiesMarker.Auto())
                    {
                        RestoreStructures(data.structures);
                        RestoreBlueprints(data.blueprints);
                        RestoreGroundResources(data.groundResources);
                        RestoreDuplicants(data.duplicants);
                        RestoreFlora(data.flora);
                    }
                    PerformanceMetricsService.EndSample(PerformanceMetric.LoadRestoreEntities, phaseStartedAt);
                }

                // O mundo físico já está pronto. Libera a recriação das tarefas,
                // mantendo o tempo pausado até o fim da restauração.
                long postStartedAt = PerformanceMetricsService.BeginSample();
                using (PostRestoreMarker.Auto())
                {
                    SaveGameRuntime.IsLoading = false;
                    RestoreTasks(data.persistentTasks);
                    RestorePrintingPodScheduler(data.printingPodScheduler);
                    RestoreDayNightCycle(data.dayNightCycle);
                    RestoreCamera(data.camera);
                    RestoreRecipeUnlocks(data.recipeUnlocks);
                    StockpileManager.Instance?.RefreshReachableResources();
                    StructureManager.Instance?.QueueExistingGroundItems();
                }
                PerformanceMetricsService.EndSample(PerformanceMetric.LoadPostRestore, postStartedAt);

                lastOperationMessage = "Jogo carregado com sucesso.";
                Debug.Log("[SaveGameService] " + lastOperationMessage);
            }
            catch (Exception exception)
            {
                loadException = exception;
            }
        }

        if (loadException != null)
        {
            lastOperationMessage =
                "Falha ao carregar: " + loadException.Message;
            Debug.LogError("[SaveGameService] " + lastOperationMessage);
        }

        SaveGameRuntime.IsLoading = false;
        Time.timeScale = previousTimeScale;
        IsAutomaticLoadInProgress = false;
        IsBusy = false;
        PerformanceMetricsService.EndSample(PerformanceMetric.Load, loadStartedAt);

        if (notifyStartupMenu)
        {
            OnStartupSessionFinished?.Invoke(
                loadException == null,
                lastOperationMessage
            );
        }
    }

    private static void ClearRuntimeWorld()
    {
        DuplicantController[] duplicants =
            FindObjectsByType<DuplicantController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (DuplicantController duplicant in duplicants)
        {
            duplicant.gameObject.SetActive(false);
            Destroy(duplicant.gameObject);
        }

        TaskManager.Instance?.ClearTasksForLoad();
        StockpileManager.Instance?.ResetRuntimeReservationsForLoad();
        BlueprintManager.Instance?.ClearBlueprintsForLoad();
        StructureManager.Instance?.ClearStructuresForLoad();
        FloraManager.Instance?.ClearForLoad();

        ResourceItem[] resources = FindObjectsByType<ResourceItem>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (ResourceItem resource in resources)
        {
            resource.Recycle();
        }
    }

    private IEnumerator RestoreGridIncrementally(
        WorldSaveData world,
        Action<Exception> reportFailure)
    {
        GridManager grid = GridManager.Instance;

        if (grid == null)
        {
            reportFailure?.Invoke(
                new InvalidOperationException("GridManager não encontrado."));
            yield break;
        }

        if (world == null)
        {
            reportFailure?.Invoke(
                new InvalidDataException("Dados do grid ausentes no save."));
            yield break;
        }

        bool usesCompactFormat = HasValidCompactWorldData(world);
        if (!usesCompactFormat && world.tiles == null)
        {
            reportFailure?.Invoke(
                new InvalidDataException("Dados das células ausentes no save."));
            yield break;
        }

        int totalTiles = usesCompactFormat
            ? checked(world.width * world.height)
            : world.tiles.Count;
        int tilesPerFrame = Mathf.Max(128, gridRestoreTilesPerFrame);
        int restoredTiles = 0;
        int contentCellCursor = 0;

        try
        {
            grid.cellSize = Mathf.Max(0.01f, world.cellSize);
            grid.BeginSnapshotRestore(world.width, world.height);
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
            yield break;
        }

        while (restoredTiles < totalTiles)
        {
            int batchEnd = Mathf.Min(
                restoredTiles + tilesPerFrame,
                totalTiles);

            try
            {
                using (RestoreGridMarker.Auto())
                using (RestoreGridBatchMarker.Auto())
                {
                    for (; restoredTiles < batchEnd; restoredTiles++)
                    {
                        if (usesCompactFormat)
                        {
                            RestoreCompactTile(
                                grid,
                                world,
                                restoredTiles,
                                ref contentCellCursor);
                        }
                        else
                        {
                            RestoreLegacyTile(
                                grid,
                                world.tiles[restoredTiles]);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                reportFailure?.Invoke(exception);
                yield break;
            }

            float progress = totalTiles > 0
                ? (float)restoredTiles / totalTiles
                : 1f;
            OnLoadProgress?.Invoke(
                progress,
                $"Restaurando mapa... {Mathf.RoundToInt(progress * 100f)}%");

            if (restoredTiles < totalTiles)
            {
                yield return null;
            }
        }

        try
        {
            // Única publicação do mundo pronto. Nav, fog, tilemaps e líquidos
            // nunca recebem um grid parcialmente restaurado.
            using (RestoreGridMarker.Auto())
            {
                // Os dados já podem ser consultados, mas o evento público só
                // será emitido depois que todos os sistemas derivados terminarem.
                grid.CompleteSnapshotRestore(false);
            }
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
        }
    }

    private static void RestoreLegacyTile(
        GridManager grid,
        TileSaveData tile)
    {
        grid.RestoreTileState(
            tile.x,
            tile.y,
            (TileType)tile.tileType,
            (FogState)tile.fogState,
            tile.liquidAmount,
            (TileType)tile.backWallTileType,
            (TileType)tile.decorationTileType,
            tile.terrainContentId,
            tile.structureContentId,
            tile.backWallContentId,
            tile.decorationContentId);
    }

    private static void RestoreCompactTile(
        GridManager grid,
        WorldSaveData world,
        int cellIndex,
        ref int contentCellCursor)
    {
        string terrainContentId = string.Empty;
        string structureContentId = string.Empty;
        string backWallContentId = string.Empty;
        string decorationContentId = string.Empty;

        List<TileContentSaveData> contentCells = world.contentCells;
        if (contentCells != null
            && contentCellCursor < contentCells.Count
            && contentCells[contentCellCursor].cellIndex == cellIndex)
        {
            TileContentSaveData content = contentCells[contentCellCursor];
            terrainContentId = content.terrainContentId;
            structureContentId = content.structureContentId;
            backWallContentId = content.backWallContentId;
            decorationContentId = content.decorationContentId;
            contentCellCursor++;
        }

        int x = cellIndex / world.height;
        int y = cellIndex % world.height;
        grid.RestoreTileState(
            x,
            y,
            (TileType)world.tileTypes[cellIndex],
            (FogState)world.fogStates[cellIndex],
            world.liquidAmounts[cellIndex],
            (TileType)world.backWallTileTypes[cellIndex],
            (TileType)world.decorationTileTypes[cellIndex],
            terrainContentId,
            structureContentId,
            backWallContentId,
            decorationContentId);
    }

    private IEnumerator RestoreWorldSystemsIncrementally(
        Action<Exception> reportFailure)
    {
        Exception phaseFailure = null;
        Action<Exception> captureFailure = exception =>
        {
            phaseFailure = exception;
            reportFailure?.Invoke(exception);
        };

        TilemapVisualizer[] tilemapVisualizers =
            FindObjectsByType<TilemapVisualizer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        for (int index = 0; index < tilemapVisualizers.Length; index++)
        {
            TilemapVisualizer visualizer = tilemapVisualizers[index];
            if (visualizer == null) continue;

            OnLoadProgress?.Invoke(
                1f,
                $"Reconstruindo Tilemaps... {index + 1}/{tilemapVisualizers.Length}");
            yield return RunLoadPhaseSafely(
                visualizer.RenderFullGridIncrementally(),
                captureFailure);
            if (phaseFailure != null) yield break;
        }

        if (FogOfWarManager.Instance != null)
        {
            OnLoadProgress?.Invoke(1f, "Reconstruindo névoa...");
            yield return RunLoadPhaseSafely(
                FogOfWarManager.Instance.InitializeFogIncrementally(),
                captureFailure);
            if (phaseFailure != null) yield break;
        }

        LiquidVisualizer liquidVisualizer =
            FindFirstObjectByType<LiquidVisualizer>();
        if (liquidVisualizer != null)
        {
            OnLoadProgress?.Invoke(1f, "Reconstruindo líquidos...");
            yield return RunLoadPhaseSafely(
                liquidVisualizer.FullRefreshIncrementally(),
                captureFailure);
            if (phaseFailure != null) yield break;
        }

        if (NavGraphGenerator.Instance != null)
        {
            OnLoadProgress?.Invoke(1f, "Reconstruindo navegação...");
            yield return RunLoadPhaseSafely(
                NavGraphGenerator.Instance.RegenerateGraphIncrementally(),
                captureFailure);
            if (phaseFailure != null) yield break;
        }

        if (GridManager.Instance == null)
        {
            reportFailure?.Invoke(
                new InvalidOperationException("GridManager não encontrado."));
            yield break;
        }

        // Este é o único OnGridRebuilt do carregamento. Os sistemas pesados
        // ignoram o handler síncrono enquanto IsLoading está ativo.
        GridManager.Instance.PublishGridRebuilt();
    }

    private static IEnumerator RunLoadPhaseSafely(
        IEnumerator phase,
        Action<Exception> reportFailure)
    {
        if (phase == null) yield break;

        while (true)
        {
            bool hasNext;
            object current = null;

            try
            {
                hasNext = phase.MoveNext();
                if (hasNext) current = phase.Current;
            }
            catch (Exception exception)
            {
                reportFailure?.Invoke(exception);
                yield break;
            }

            if (!hasNext) yield break;
            yield return current;
        }
    }

    private static void RestoreStructures(
        List<StructureSaveData> structures)
    {
        if (StructureManager.Instance == null)
        {
            return;
        }

        foreach (StructureSaveData savedStructure in structures)
        {
            Vector2Int position = new Vector2Int(
                savedStructure.x,
                savedStructure.y
            );
            TileType tileType = (TileType)savedStructure.tileType;

            string definitionId = savedStructure.definitionId;
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                definitionId = GridManager.Instance?.GetContentId(
                    position.x,
                    position.y,
                    GridLayer.Structure);
            }

            BuildDefinitionSO definition =
                BuildCatalogService.Instance?.GetById(definitionId);
            if (definition == null)
            {
                definition = BuildCatalogService.Instance?.FindLegacy(
                    tileType,
                    GridLayer.Structure);
            }

            if (definition != null && definition.HasPhysicalPrefab)
            {
                StructureManager.Instance.SpawnStructure(position, definition);
            }
            else
            {
                StructureManager.Instance.SpawnStructure(position, tileType);
            }

            StorageStructure storage =
                StructureManager.Instance.GetStorageAt(position);

            if (storage == null)
            {
                continue;
            }

            storage.maxCapacityPerResource = Mathf.Max(
                1,
                savedStructure.maxCapacityPerResource
            );

            Dictionary<ResourceType, int> restoredItems =
                new Dictionary<ResourceType, int>();

            foreach (ResourceAmountSaveData item in savedStructure.storedItems)
            {
                restoredItems[(ResourceType)item.resourceType] = item.amount;
            }

            storage.RestoreStoredItems(restoredItems);
            storage.RestoreDismantleState(savedStructure.isBeingDismantled);
        }
    }

    private static void RestoreBlueprints(
        List<BlueprintSaveData> blueprints)
    {
        if (BlueprintManager.Instance == null)
        {
            return;
        }

        foreach (BlueprintSaveData savedBlueprint in blueprints)
        {
            BlueprintManager.Instance.RestoreBlueprint(
                new Vector2Int(savedBlueprint.x, savedBlueprint.y),
                (TileType)savedBlueprint.targetTileType,
                (ResourceType)savedBlueprint.requiredResource,
                savedBlueprint.requiredAmount,
                savedBlueprint.totalWorkRequired,
                savedBlueprint.deliveredAmount,
                savedBlueprint.currentWorkDone,
                (GridLayer)savedBlueprint.buildLayer,
                savedBlueprint.buildDefinitionId
            );
        }
    }

    private static void RestoreGroundResources(
        List<ResourceItemSaveData> resources)
    {
        if (ItemSpawner.Instance == null)
        {
            return;
        }

        foreach (ResourceItemSaveData savedResource in resources)
        {
            ItemSpawner.Instance.RestoreResource(
                (ResourceType)savedResource.resourceType,
                new Vector3(
                    savedResource.worldX,
                    savedResource.worldY,
                    savedResource.worldZ
                ),
                savedResource.amount
            );
        }
    }

    private void RestoreDuplicants(List<DuplicantSaveData> duplicants)
    {
        DuplicantSpawnService spawnService = GetDuplicantSpawnService();

        if (spawnService == null)
        {
            throw new InvalidOperationException(
                "DuplicantSpawnService não encontrado."
            );
        }

        foreach (DuplicantSaveData savedDuplicant in duplicants)
        {
            DuplicantController duplicant = spawnService.SpawnFromSave(
                savedDuplicant.definitionId,
                new Vector2Int(
                    savedDuplicant.gridX,
                    savedDuplicant.gridY
                )
            );

            if (duplicant == null)
            {
                Debug.LogWarning(
                    "[SaveGameService] Um duplicant não pôde ser restaurado: "
                    + savedDuplicant.definitionId
                );
                continue;
            }

            if (!string.IsNullOrWhiteSpace(savedDuplicant.displayName))
            {
                duplicant.gameObject.name = savedDuplicant.displayName;
            }

            DuplicantInventory inventory =
                duplicant.GetComponent<DuplicantInventory>();

            if (inventory != null)
            {
                if (savedDuplicant.inventoryCapacity > 0)
                {
                    inventory.maxCapacity = savedDuplicant.inventoryCapacity;
                }

                inventory.Restore(
                    savedDuplicant.hasCarriedResource
                        ? (ResourceType?)savedDuplicant.carriedResourceType
                        : null,
                    savedDuplicant.carriedAmount
                );
            }

            DuplicantVitals vitals =
                duplicant.GetComponent<DuplicantVitals>();

            if (vitals != null)
            {
                vitals.Restore(
                    savedDuplicant.currentEnergy,
                    savedDuplicant.currentHunger,
                    savedDuplicant.hasVitals
                );
            }

            // Não restaura a referência/reserva antiga da cama. O cérebro
            // procura uma cama válida novamente quando o load terminar.
            duplicant.Brain?.RestoreBedRestIntent(
                savedDuplicant.wasRestingInBed);

            DuplicantStatusEffects statusEffects =
                duplicant.GetComponent<DuplicantStatusEffects>();
            statusEffects?.Restore(
                savedDuplicant.rawFoodDiscomfortRemaining,
                savedDuplicant.rawFoodWorkPenaltyPercent);
        }
    }

    private static void RestoreFlora(List<FloraSaveData> flora)
    {
        if (FloraManager.Instance == null || flora == null) return;

        foreach (FloraSaveData saved in flora)
        {
            FloraManager.Instance.SpawnFromSave(
                saved.floraId,
                new Vector2Int(saved.gridX, saved.gridY),
                saved.availablePortions);
        }
    }

    private static void RestoreTasks(List<TaskSaveData> tasks)
    {
        if (TaskManager.Instance == null)
        {
            return;
        }

        foreach (TaskSaveData savedTask in tasks)
        {
            TaskType restoredType = (TaskType)savedTask.taskType;
            Vector2Int restoredPosition = new Vector2Int(
                savedTask.gridX,
                savedTask.gridY);

            if (restoredType == TaskType.Harvest)
            {
                FloraEntity flora = FloraManager.Instance?.GetFloraAt(
                    restoredPosition);
                if (flora != null)
                {
                    TaskManager.Instance.AddHarvestTask(
                        flora,
                        savedTask.priority);
                }
                continue;
            }

            Task task = new Task(
                restoredPosition,
                restoredType,
                (TileType)savedTask.buildTileType,
                null,
                savedTask.priority,
                ResolveSavedTaskLayer(savedTask)
            );

            TaskManager.Instance.AddTask(task);
        }

        ConstructionBlueprint[] blueprints =
            FindObjectsByType<ConstructionBlueprint>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (ConstructionBlueprint blueprint in blueprints)
        {
            blueprint.CheckTaskState();
        }
    }

    private static GridLayer ResolveSavedTaskLayer(TaskSaveData savedTask)
    {
        GridLayer savedLayer = System.Enum.IsDefined(
            typeof(GridLayer),
            savedTask.targetLayer)
            ? (GridLayer)savedTask.targetLayer
            : GridLayer.Terrain;

        // Saves antigos não possuíam targetLayer. Estruturas registradas no
        // footprint devem continuar sendo restauradas corretamente.
        if ((TaskType)savedTask.taskType == TaskType.Dismantle
            && savedLayer == GridLayer.Terrain
            && GridManager.Instance != null
            && GridManager.Instance.GetOccupantAt(
                new Vector2Int(savedTask.gridX, savedTask.gridY),
                GridLayer.Structure) != null)
        {
            return GridLayer.Structure;
        }

        return savedLayer;
    }

    private static void RestorePrintingPodScheduler(
        PrintingPodSchedulerSaveData savedScheduler)
    {
        if (savedScheduler == null
            || !savedScheduler.hasState
            || PrintingPodScheduler.Instance == null)
        {
            return;
        }

        PrintingPodScheduler.Instance.RestoreState(
            savedScheduler.timeRemaining,
            savedScheduler.isOfferReady,
            savedScheduler.isTimerRunning,
            savedScheduler.hasGeneratedFirstOffer
        );
    }

    private void RestoreCamera(CameraSaveData savedCamera)
    {
        if (savedCamera == null || !savedCamera.hasState)
        {
            return;
        }

        GetCameraController()?.RestoreView(
            new Vector3(
                savedCamera.worldX,
                savedCamera.worldY,
                savedCamera.worldZ
            ),
            savedCamera.orthographicSize
        );
    }

    private static void RestoreDayNightCycle(
        DayNightCycleSaveData savedCycle)
    {
        DayNightCycleManager.Instance?.RestoreState(savedCycle);
    }

    private static void RestoreRecipeUnlocks(RecipeUnlockSaveData savedState)
    {
        RecipeUnlockService service = RecipeUnlockService.Instance;
        if (service == null) return;

        // Saves anteriores a esta fase não possuem o bloco de desbloqueios.
        if (savedState == null || !savedState.hasState)
        {
            service.ResetToConfiguredDefaults();
            return;
        }

        service.RestoreState(
            savedState.unlockedDefinitionIds,
            savedState.unlockedLegacyTileTypes);
    }

    private static string[] GetLoadCandidatePaths(int slot)
    {
        if (slot == 1)
        {
            return new[]
            {
                GetSavePath(slot),
                GetBackupPath(slot),
                GetLegacySavePath(),
                GetLegacyBackupPath()
            };
        }

        return new[]
        {
            GetSavePath(slot),
            GetBackupPath(slot)
        };
    }

    private static LoadBackgroundResult ReadSaveInBackground(
        string[] candidatePaths)
    {
        LoadBackgroundResult result = new LoadBackgroundResult();
        string lastError = "Nenhum arquivo de save encontrado.";
        bool foundFile = false;

        for (int index = 0; index < candidatePaths.Length; index++)
        {
            string path = candidatePaths[index];
            if (!File.Exists(path)) continue;

            foundFile = true;

            try
            {
                long phaseStartedAt =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                string json;
                using (ReadFileMarker.Auto())
                    json = File.ReadAllText(path);
                result.readMilliseconds +=
                    ElapsedMilliseconds(phaseStartedAt);

                phaseStartedAt =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                SaveGameData data;
                using (DeserializeMarker.Auto())
                    data = JsonUtility.FromJson<SaveGameData>(json);
                result.deserializeMilliseconds +=
                    ElapsedMilliseconds(phaseStartedAt);

                if (!IsValid(data, out lastError)) continue;

                result.data = data;
                result.usedFallback = index > 0;
                return result;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }
        }

        result.error = foundFile
            ? "Nenhum save válido encontrado: " + lastError
            : "Nenhum arquivo de save encontrado.";
        return result;
    }

    private static bool IsValid(SaveGameData data, out string error)
    {
        if (data == null || data.world == null)
        {
            error = "Estrutura do save inválida.";
            return false;
        }

        if (data.saveVersion < MinimumSupportedSaveVersion
            || data.saveVersion > CurrentSaveVersion)
        {
            error = "Versão do save incompatível: " + data.saveVersion;
            return false;
        }

        int expectedTileCount = data.world.width * data.world.height;

        bool hasValidLegacyData = data.world.tiles != null
            && data.world.tiles.Count == expectedTileCount;
        bool hasValidCompactData = HasValidCompactWorldData(data.world);

        if (data.world.width <= 0
            || data.world.height <= 0
            || (!hasValidLegacyData && !hasValidCompactData))
        {
            error = "Snapshot do Grid incompleto.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool HasValidCompactWorldData(WorldSaveData world)
    {
        if (world == null || world.width <= 0 || world.height <= 0)
        {
            return false;
        }

        int expectedTileCount;
        try
        {
            expectedTileCount = checked(world.width * world.height);
        }
        catch (OverflowException)
        {
            return false;
        }

        bool arraysAreValid = world.tileTypes != null
            && world.tileTypes.Length == expectedTileCount
            && world.backWallTileTypes != null
            && world.backWallTileTypes.Length == expectedTileCount
            && world.decorationTileTypes != null
            && world.decorationTileTypes.Length == expectedTileCount
            && world.fogStates != null
            && world.fogStates.Length == expectedTileCount
            && world.liquidAmounts != null
            && world.liquidAmounts.Length == expectedTileCount;

        if (!arraysAreValid || world.contentCells == null)
        {
            return false;
        }

        int previousCellIndex = -1;
        for (int index = 0; index < world.contentCells.Count; index++)
        {
            int cellIndex = world.contentCells[index].cellIndex;
            if (cellIndex <= previousCellIndex
                || cellIndex < 0
                || cellIndex >= expectedTileCount)
            {
                return false;
            }

            previousCellIndex = cellIndex;
        }

        return true;
    }

    private static void WriteAtomically(
        string savePath,
        string backupPath,
        string json)
    {
        string directory = Path.GetDirectoryName(savePath);
        Directory.CreateDirectory(directory);

        string temporaryPath = savePath + ".tmp";
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(savePath))
        {
            File.Copy(savePath, backupPath, true);
            File.Delete(savePath);
        }

        File.Move(temporaryPath, savePath);
    }

    private static string CreateSlotMetadataJson(SaveGameData data)
    {
        SaveSlotMetadata metadata = new SaveSlotMetadata
        {
            saveVersion = data.saveVersion,
            savedAtUtc = data.savedAtUtc,
            worldWidth = data.world != null ? data.world.width : 0,
            worldHeight = data.world != null ? data.world.height : 0
        };

        return JsonUtility.ToJson(metadata, false);
    }

    private static void WriteSlotMetadata(string path, string metadataJson)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, metadataJson);

        if (File.Exists(path)) File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private static string GetSavePath(int slot)
    {
        return Path.Combine(
            Application.persistentDataPath,
            SaveDirectoryName,
            $"colony-save-slot-{slot}.json"
        );
    }

    private static string GetBackupPath(int slot)
    {
        return Path.Combine(
            Application.persistentDataPath,
            SaveDirectoryName,
            $"colony-save-slot-{slot}.backup.json"
        );
    }

    private static string GetMetadataPath(int slot)
    {
        return Path.Combine(
            Application.persistentDataPath,
            SaveDirectoryName,
            $"colony-save-slot-{slot}.metadata.json"
        );
    }

    private static string GetLegacySavePath()
    {
        return Path.Combine(
            Application.persistentDataPath,
            SaveDirectoryName,
            LegacySaveFileName
        );
    }

    private static string GetLegacyBackupPath()
    {
        return Path.Combine(
            Application.persistentDataPath,
            SaveDirectoryName,
            LegacyBackupFileName
        );
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

    private CameraController GetCameraController()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }

        return cameraController;
    }
}
