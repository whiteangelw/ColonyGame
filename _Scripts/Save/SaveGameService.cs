using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class SaveGameService : MonoBehaviour
{
    public static SaveGameService Instance { get; private set; }

    private const int CurrentSaveVersion = 1;
    public const int MaxSaveSlots = 3;
    private const string SaveDirectoryName = "Saves";
    private const string LegacySaveFileName = "colony-save.json";
    private const string LegacyBackupFileName = "colony-save.backup.json";

    [Header("Referências")]
    [SerializeField] private DuplicantSpawnService duplicantSpawnService;
    [SerializeField] private CameraController cameraController;

    [Header("Inicialização")]
    [Tooltip("Carrega automaticamente um save válido antes de gerar um mundo novo.")]
    [SerializeField] private bool loadAutomaticallyOnStart = true;

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

        if (!TryReadSaveFromSlot(slot, out SaveGameData data, out _))
        {
            return $"Slot {slot} — Vazio";
        }

        if (DateTime.TryParse(data.savedAtUtc, out DateTime savedAt))
        {
            return $"Slot {slot} — {savedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
        }

        return $"Slot {slot} — Colônia salva";
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

        if (!TryReadSave(out SaveGameData data, out string error))
        {
            lastOperationMessage =
                "Nenhum save válido. Uma nova colônia será criada.";
            Debug.Log(
                "[SaveGameService] " + lastOperationMessage
            );
            return false;
        }

        IsAutomaticLoadInProgress = true;
        lastOperationMessage = "Carregamento automático iniciado.";
        StartCoroutine(LoadRoutine(data));
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

        if (!TryReadSave(out SaveGameData data, out string error))
        {
            lastOperationMessage = error;
            OnStartupSessionFinished?.Invoke(false, error);
            return false;
        }

        IsWaitingForStartupChoice = false;
        IsAutomaticLoadInProgress = true;
        lastOperationMessage = "Carregando colônia...";
        StartCoroutine(LoadRoutine(data, true));
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

        try
        {
            SaveGameData data = CaptureGame();
            string json = JsonUtility.ToJson(data, true);
            WriteAtomically(json);
            lastOperationMessage = "Jogo salvo com sucesso.";
            Debug.Log($"[SaveGameService] {lastOperationMessage} {SavePath}");
        }
        catch (Exception exception)
        {
            lastOperationMessage = "Falha ao salvar: " + exception.Message;
            Debug.LogError("[SaveGameService] " + lastOperationMessage);
        }
    }

    [ContextMenu("Load Game")]
    public void LoadGame()
    {
        if (IsBusy || SaveGameRuntime.IsLoading)
        {
            return;
        }

        if (!TryReadSave(out SaveGameData data, out string error))
        {
            lastOperationMessage = error;
            Debug.LogError("[SaveGameService] " + error);
            return;
        }

        StartCoroutine(LoadRoutine(data));
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
        CapturePersistentTasks(data);
        CapturePrintingPodScheduler(data);
        CaptureCamera(data);
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

        for (int x = 0; x < grid.width; x++)
        {
            for (int y = 0; y < grid.height; y++)
            {
                Tile tile = grid.GetTile(x, y);

                if (tile == null)
                {
                    continue;
                }

                data.world.tiles.Add(new TileSaveData
                {
                    x = x,
                    y = y,
                    tileType = (int)tile.type,
                    fogState = (int)tile.fogState,
                    liquidAmount = tile.liquidAmount
                });
            }
        }
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
            StructureSaveData savedStorage = new StructureSaveData
            {
                x = storage.GridPosition.x,
                y = storage.GridPosition.y,
                tileType = (int)TileType.Chest,
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
            data.structures.Add(new StructureSaveData
            {
                x = printingPod.GridPosition.x,
                y = printingPod.GridPosition.y,
                tileType = (int)TileType.PrintingPod
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
                    : 0
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
                    && task.type != TaskType.Dismantle))
            {
                continue;
            }

            data.persistentTasks.Add(new TaskSaveData
            {
                taskType = (int)task.type,
                gridX = task.gridPosition.x,
                gridY = task.gridPosition.y,
                buildTileType = (int)task.buildTileType,
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

    private IEnumerator LoadRoutine(
        SaveGameData data,
        bool notifyStartupMenu = false)
    {
        IsBusy = true;
        SaveGameRuntime.IsLoading = true;
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        Exception loadException = null;

        try
        {
            ClearRuntimeWorld();
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
            try
            {
                RestoreGrid(data.world);
                RestoreStructures(data.structures);
                RestoreBlueprints(data.blueprints);
                RestoreGroundResources(data.groundResources);
                RestoreDuplicants(data.duplicants);

                // O mundo físico já está pronto. Libera a recriação das tarefas,
                // mantendo o tempo pausado até o fim da restauração.
                SaveGameRuntime.IsLoading = false;
                RestoreTasks(data.persistentTasks);
                RestorePrintingPodScheduler(data.printingPodScheduler);
                RestoreCamera(data.camera);

                StockpileManager.Instance?.RefreshReachableResources();
                StructureManager.Instance?.QueueExistingGroundItems();

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

        ResourceItem[] resources = FindObjectsByType<ResourceItem>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (ResourceItem resource in resources)
        {
            resource.Recycle();
        }
    }

    private static void RestoreGrid(WorldSaveData world)
    {
        GridManager grid = GridManager.Instance;

        if (grid == null)
        {
            throw new InvalidOperationException("GridManager não encontrado.");
        }

        grid.cellSize = Mathf.Max(0.01f, world.cellSize);
        grid.BeginSnapshotRestore(world.width, world.height);

        foreach (TileSaveData tile in world.tiles)
        {
            grid.RestoreTileState(
                tile.x,
                tile.y,
                (TileType)tile.tileType,
                (FogState)tile.fogState,
                tile.liquidAmount
            );
        }

        grid.CompleteSnapshotRestore();
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

            StructureManager.Instance.SpawnStructure(position, tileType);

            if (tileType != TileType.Chest)
            {
                continue;
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
                savedBlueprint.currentWorkDone
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
            Task task = new Task(
                new Vector2Int(savedTask.gridX, savedTask.gridY),
                (TaskType)savedTask.taskType,
                (TileType)savedTask.buildTileType,
                null,
                savedTask.priority
            );

            TaskManager.Instance.AddTask(task);

            if (task.type == TaskType.Dismantle)
            {
                StorageStructure storage =
                    StructureManager.Instance?.GetStorageAt(task.gridPosition);
                storage?.RestoreDismantleState(true);
            }
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

    private static void RestorePrintingPodScheduler(
        PrintingPodSchedulerSaveData savedScheduler)
    {
        if (!savedScheduler.hasState
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
        if (!savedCamera.hasState)
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

    private bool TryReadSave(
        out SaveGameData data,
        out string error)
    {
        return TryReadSaveFromSlot(activeSlot, out data, out error);
    }

    private bool TryReadSaveFromSlot(
        int slot,
        out SaveGameData data,
        out string error)
    {
        string primaryPath = GetSavePath(slot);
        string backupPath = GetBackupPath(slot);

        if (TryReadFile(primaryPath, out data, out error))
        {
            return true;
        }

        string primaryError = error;

        if (TryReadFile(backupPath, out data, out error))
        {
            Debug.LogWarning(
                "[SaveGameService] Save principal inválido. Backup carregado."
            );
            return true;
        }

        if (slot == 1)
        {
            if (TryReadFile(GetLegacySavePath(), out data, out error)
                || TryReadFile(GetLegacyBackupPath(), out data, out error))
            {
                return true;
            }
        }

        error = "Nenhum save válido encontrado. Principal: "
            + primaryError + " Backup: " + error;
        return false;
    }

    private static bool TryReadFile(
        string path,
        out SaveGameData data,
        out string error)
    {
        data = null;

        if (!File.Exists(path))
        {
            error = "Arquivo não encontrado.";
            return false;
        }

        try
        {
            data = JsonUtility.FromJson<SaveGameData>(
                File.ReadAllText(path)
            );

            if (!IsValid(data, out error))
            {
                data = null;
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            data = null;
            return false;
        }
    }

    private static bool IsValid(SaveGameData data, out string error)
    {
        if (data == null || data.world == null)
        {
            error = "Estrutura do save inválida.";
            return false;
        }

        if (data.saveVersion != CurrentSaveVersion)
        {
            error = "Versão do save incompatível: " + data.saveVersion;
            return false;
        }

        int expectedTileCount = data.world.width * data.world.height;

        if (data.world.width <= 0
            || data.world.height <= 0
            || data.world.tiles == null
            || data.world.tiles.Count != expectedTileCount)
        {
            error = "Snapshot do Grid incompleto.";
            return false;
        }

        error = null;
        return true;
    }

    private void WriteAtomically(string json)
    {
        string directory = Path.GetDirectoryName(SavePath);
        Directory.CreateDirectory(directory);

        string temporaryPath = SavePath + ".tmp";
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(SavePath))
        {
            File.Copy(SavePath, GetBackupPath(activeSlot), true);
            File.Delete(SavePath);
        }

        File.Move(temporaryPath, SavePath);
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
