using System.Collections.Generic;
using UnityEngine;

public class StructureManager : Singleton<StructureManager>
{
    [Header("Fallback legado — não usar em novas construções")]
    [Tooltip("Usado somente para carregar conteúdo antigo sem Definition Id.")]
    [SerializeField] private GameObject chestPrefab;
    [Tooltip("Usado somente para carregar conteúdo antigo sem Definition Id.")]
    [SerializeField] private GameObject printingPodPrefab;

    private readonly Dictionary<Vector2Int, StorageStructure> activeStorages = new Dictionary<Vector2Int, StorageStructure>();
    private readonly Dictionary<Vector2Int, PrintingPod> activePrintingPods =
        new Dictionary<Vector2Int, PrintingPod>();
    private readonly Dictionary<Vector2Int, ConfiguredStructure> activeConfiguredStructures =
        new Dictionary<Vector2Int, ConfiguredStructure>();

    public PrintingPod GetPrintingPodAt(Vector2Int position)
    {
        activePrintingPods.TryGetValue(position, out PrintingPod printingPod);

        if (printingPod == null && GridManager.Instance != null)
        {
            printingPod = GridManager.Instance.GetOccupantAt(position)
                as PrintingPod;
        }

        return printingPod;
    }

    public void RegisterPrintingPod(Vector2Int position, PrintingPod printingPod)
    {
        if (printingPod == null)
        {
            return;
        }

        activePrintingPods[position] = printingPod;
        printingPod.SetGridPosition(position);

        if (printingPod.GetComponent<ConfiguredStructure>() == null
            && GridManager.Instance != null
            && !GridManager.Instance.RegisterFootprint(
                printingPod,
                position,
                ResolveFootprint(position, TileType.PrintingPod),
                false))
        {
            Debug.LogError(
                $"[StructureManager] Área do PrintingPod em {position} está ocupada.",
                printingPod);
        }
    }

    public void UnregisterPrintingPod(Vector2Int position)
    {
        if (activePrintingPods.TryGetValue(position, out PrintingPod printingPod))
        {
            GridManager.Instance?.UnregisterFootprint(printingPod);
        }

        activePrintingPods.Remove(position);
    }

    public void RegisterStorage(Vector2Int pos, StorageStructure storage)
    {
        if (storage == null) return;

        if (activeStorages.ContainsKey(pos))
        {
            activeStorages[pos] = storage;
        }
        else
        {
            activeStorages.Add(pos, storage);
        }

        storage.SetGridPosition(pos);
        if (storage.GetComponent<ConfiguredStructure>() == null
            && GridManager.Instance != null
            && !GridManager.Instance.RegisterFootprint(
                storage,
                pos,
                ResolveFootprint(pos, TileType.Chest),
                false))
        {
            Debug.LogError(
                $"[StructureManager] Área do baú em {pos} está ocupada.",
                storage);
        }

        QueueExistingGroundItems();
    }

    public void UnregisterStorage(Vector2Int pos)
    {
        if (activeStorages.TryGetValue(pos, out StorageStructure storage))
        {
            GridManager.Instance?.UnregisterFootprint(storage);
            activeStorages.Remove(pos);
        }
    }

    public StorageStructure GetStorageAt(Vector2Int position)
    {
        activeStorages.TryGetValue(position, out var storage);

        if (storage == null && GridManager.Instance != null)
        {
            storage = GridManager.Instance.GetOccupantAt(position)
                as StorageStructure;
        }

        return storage;
    }

    public List<StorageStructure> GetRegisteredStorages()
    {
        List<StorageStructure> storages =
            new List<StorageStructure>();

        foreach (StorageStructure storage in activeStorages.Values)
        {
            if (storage != null)
            {
                storages.Add(storage);
            }
        }

        return storages;
    }

    public List<PrintingPod> GetRegisteredPrintingPods()
    {
        List<PrintingPod> printingPods = new List<PrintingPod>();

        foreach (PrintingPod printingPod in activePrintingPods.Values)
        {
            if (printingPod != null)
            {
                printingPods.Add(printingPod);
            }
        }

        return printingPods;
    }

    public void ClearStructuresForLoad()
    {
        HashSet<GameObject> destroyedObjects = new HashSet<GameObject>();

        foreach (ConfiguredStructure structure in activeConfiguredStructures.Values)
        {
            if (structure == null) continue;
            structure.ShutdownBehaviours();
            GridManager.Instance?.UnregisterFootprint(structure);
            destroyedObjects.Add(structure.gameObject);
            structure.gameObject.SetActive(false);
            Destroy(structure.gameObject);
        }

        foreach (StorageStructure storage in activeStorages.Values)
        {
            if (storage != null && !destroyedObjects.Contains(storage.gameObject))
            {
                GridManager.Instance?.UnregisterFootprint(storage);
                storage.gameObject.SetActive(false);
                Destroy(storage.gameObject);
            }
        }

        foreach (PrintingPod printingPod in activePrintingPods.Values)
        {
            if (printingPod != null && !destroyedObjects.Contains(printingPod.gameObject))
            {
                GridManager.Instance?.UnregisterFootprint(printingPod);
                printingPod.SetOperational(false);
                printingPod.gameObject.SetActive(false);
                Destroy(printingPod.gameObject);
            }
        }

        activeStorages.Clear();
        activePrintingPods.Clear();
        activeConfiguredStructures.Clear();
    }

    public List<ConfiguredStructure> GetRegisteredConfiguredStructures()
    {
        List<ConfiguredStructure> result = new List<ConfiguredStructure>();
        foreach (ConfiguredStructure structure in activeConfiguredStructures.Values)
        {
            if (structure != null) result.Add(structure);
        }
        return result;
    }

    public bool RegisterConfiguredStructure(
        ConfiguredStructure structure,
        BuildDefinitionSO definition)
    {
        if (structure == null || definition == null || GridManager.Instance == null)
        {
            return false;
        }

        Vector2Int position = structure.GridPosition;
        if (!GridManager.Instance.RegisterFootprint(
                structure,
                position,
                definition.GetFootprint(),
                false))
        {
            Debug.LogError(
                $"[StructureManager] Falha ao registrar '{definition.DefinitionId}' em {position}.",
                structure);
            return false;
        }

        activeConfiguredStructures[position] = structure;
        return true;
    }

    public void SpawnStructure(Vector2Int position, BuildDefinitionSO definition)
    {
        if (definition == null || definition.Prefab == null || GridManager.Instance == null)
        {
            return;
        }

        if (GridManager.Instance.GetOccupantAt(position, GridLayer.Structure) != null)
        {
            return;
        }

        GameObject obj = Instantiate(
            definition.Prefab,
            GridManager.Instance.GridToWorldPosition(position),
            Quaternion.identity);
        ConfiguredStructure structure = obj.GetComponent<ConfiguredStructure>();
        if (structure == null)
        {
            Debug.LogError(
                $"[StructureManager] O prefab de '{definition.DefinitionId}' precisa de ConfiguredStructure no objeto raiz.",
                obj);
            Destroy(obj);
            return;
        }

        structure.Initialize(position, definition);
        if (!structure.IsInitialized)
        {
            Destroy(obj);
        }
    }

    public List<StorageStructure> GetStoragesWithResource(
        ResourceType type)
    {
        List<StorageStructure> storages =
            new List<StorageStructure>();

        foreach (StorageStructure storage in activeStorages.Values)
        {
            if (storage != null
                && !storage.IsBeingDismantled
                && storage.GetLocalAmount(type) > 0)
            {
                storages.Add(storage);
            }
        }

        return storages;
    }

    public IStorage GetAvailableStorageFor(ResourceType type, int amount)
    {
        foreach (var kvp in activeStorages)
        {
            if (kvp.Value != null && kvp.Value.CanStoreItem(type, amount))
            {
                return kvp.Value;
            }
        }
        return null;
    }

    public void QueueExistingGroundItems()
    {
        if (TaskManager.Instance == null)
        {
            return;
        }

        ResourceItem[] items = Object.FindObjectsByType<ResourceItem>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (ResourceItem item in items)
        {
            TaskManager.Instance.AddHaulTask(item);
        }
    }

    public bool TryReserveStorage(
        ResourceType type,
        int amount,
        out IStorage reservedStorage)
    {
        reservedStorage = null;

        if (amount <= 0)
        {
            return false;
        }

        foreach (var kvp in activeStorages)
        {
            StorageStructure storage = kvp.Value;

            if (storage != null && storage.TryReserveSpace(type, amount))
            {
                reservedStorage = storage;
                return true;
            }
        }

        return false;
    }

    public bool HasReachableStorageFor(
        ResourceType type,
        int amount,
        Vector2Int fromPosition)
    {
        return FindBestReachableStorage(
            type,
            amount,
            fromPosition
        ) != null;
    }

    public bool TryReserveReachableStorage(
        ResourceType type,
        int amount,
        Vector2Int fromPosition,
        out IStorage reservedStorage)
    {
        reservedStorage = null;

        StorageStructure storage = FindBestReachableStorage(
            type,
            amount,
            fromPosition
        );

        if (storage == null || !storage.TryReserveSpace(type, amount))
        {
            return false;
        }

        reservedStorage = storage;
        return true;
    }

    private StorageStructure FindBestReachableStorage(
        ResourceType type,
        int amount,
        Vector2Int fromPosition)
    {
        if (amount <= 0)
        {
            return null;
        }

        StorageStructure bestStorage = null;
        int shortestPathLength = int.MaxValue;

        foreach (StorageStructure storage in activeStorages.Values)
        {
            if (storage == null || !storage.CanStoreItem(type, amount))
            {
                continue;
            }

            List<Vector2Int> path =
                TaskNavigationUtility.GetPathToInteractionPosition(
                    fromPosition,
                    storage.GridPosition
                );

            if (path == null || path.Count >= shortestPathLength)
            {
                continue;
            }

            bestStorage = storage;
            shortestPathLength = path.Count;
        }

        return bestStorage;
    }

    public int TryWithdrawResource(ResourceType type, int requestedAmount)
    {
        int remainingAmount = Mathf.Max(0, requestedAmount);
        int withdrawnAmount = 0;

        foreach (var kvp in activeStorages)
        {
            if (remainingAmount <= 0)
            {
                break;
            }

            StorageStructure storage = kvp.Value;

            if (storage == null || storage.IsBeingDismantled)
            {
                continue;
            }

            int amountFromStorage = Mathf.Min(
                remainingAmount,
                storage.GetLocalAmount(type)
            );

            if (amountFromStorage <= 0
                || !storage.WithdrawItem(type, amountFromStorage))
            {
                continue;
            }

            withdrawnAmount += amountFromStorage;
            remainingAmount -= amountFromStorage;
        }

        return withdrawnAmount;
    }

    public void SpawnStructure(Vector2Int position, TileType type)
    {
        if (GridManager.Instance == null) return;

        // Se já existe um baú nesta posição, não instancia outro
        if (GetStorageAt(position) != null) return;

        if (type == TileType.Chest && chestPrefab != null)
        {
            float cs = GridManager.Instance.cellSize;
            // Centraliza o transform exatamente no centro da célula do Grid
            Vector3 spawnPos = new Vector3(position.x * cs + cs / 2f, position.y * cs + cs / 2f, 0);

            GameObject obj = Instantiate(chestPrefab, spawnPos, Quaternion.identity);

            if (obj.TryGetComponent<StorageStructure>(out var storage))
            {
                // Registra explicitamente a posição exata informada no grid
                RegisterStorage(position, storage);
            }
        }
        else if (type == TileType.PrintingPod
            && printingPodPrefab != null
            && PrintingPod.Instance == null
            && GetPrintingPodAt(position) == null)
        {
            Vector3 spawnPos = GridManager.Instance.GridToWorldPosition(position);
            GameObject obj = Instantiate(
                printingPodPrefab,
                spawnPos,
                Quaternion.identity
            );

            if (obj.TryGetComponent<PrintingPod>(out PrintingPod printingPod))
            {
                RegisterPrintingPod(position, printingPod);

                if (SaveGameRuntime.IsLoading)
                {
                    printingPod.SetOperational(true);
                }
            }
            else
            {
                Debug.LogError(
                    "[StructureManager] O prefab da máquina não possui PrintingPod."
                );
                Destroy(obj);
            }
        }
    }

    public void DismantleStructureAt(Vector2Int position)
    {
        if (GridManager.Instance == null) return;

        PrintingPod printingPod = GetPrintingPodAt(position);

        if (printingPod != null)
        {
            DismantlePrintingPod(printingPod.GridPosition, printingPod);
            return;
        }

        ConfiguredStructure configured =
            GridManager.Instance.GetOccupantAt(position, GridLayer.Structure)
                as ConfiguredStructure;

        if (configured != null)
        {
            PrintingPod configuredPod = configured.GetComponent<PrintingPod>();
            if (configuredPod != null)
            {
                DismantlePrintingPod(configured.GridPosition, configuredPod);
                return;
            }

            StorageStructure configuredStorage = configured.GetComponent<StorageStructure>();
            if (configuredStorage != null)
            {
                DismantleStorage(configured.GridPosition, configuredStorage, configured);
                return;
            }

            DismantleConfiguredStructure(configured);
            return;
        }

        StorageStructure storage = GetStorageAt(position);

        // A estrutura já foi marcada quando a tarefa foi criada.
        if (storage == null) return;

        DismantleStorage(storage.GridPosition, storage, null);
    }

    private void DismantleStorage(
        Vector2Int position,
        StorageStructure storage,
        ConfiguredStructure configured)
    {

        if (!storage.IsBeingDismantled)
        {
            storage.MarkAsDismantled();
        }

        float cs = GridManager.Instance.cellSize;
        Vector3 spawnPos = new Vector3(position.x * cs + cs / 2f, position.y * cs + cs / 2f, 0);

        // O conteúdo guardado não faz parte do reembolso da construção.
        var storedItems = storage.DrainForDismantle();
        foreach (var kvp in storedItems)
        {
            if (kvp.Value > 0)
            {
                ItemSpawner.Instance?.SpawnResource(kvp.Key, spawnPos, kvp.Value);

                GameEvents.TriggerFloatingTextRequested(
                    $"Conteúdo: +{kvp.Value} {kvp.Key}",
                    spawnPos,
                    Color.white);
            }
        }

        ResolveConstructionRefund(
            position,
            TileType.Chest,
            out ResourceType reqResource,
            out int refundAmount);

        if (refundAmount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(reqResource, spawnPos, refundAmount);

            GameEvents.TriggerFloatingTextRequested(
                $"Reembolso: +{refundAmount} {reqResource}",
                spawnPos,
                Color.green);
        }

        // 4. DESREGISTRA E LIMPA A CÉLULA NO GRID
        UnregisterStorage(position);
        if (configured != null)
        {
            activeConfiguredStructures.Remove(position);
            GridManager.Instance.UnregisterFootprint(configured);
        }
        GridManager.Instance.SetTileType(position.x, position.y, TileType.Empty);

        // 5. DESTRÓI O GAMEOBJECT FÍSICO
        Destroy(storage.gameObject);

        StockpileManager.Instance?.RefreshReachableResources();

        // 6. FEEDBACK VISUAL
        GameEvents.TriggerFloatingTextRequested("Estrutura Desmontada", spawnPos, Color.yellow);
    }

    private void DismantleConfiguredStructure(ConfiguredStructure structure)
    {
        Vector2Int anchor = structure.GridPosition;
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(structure.DefinitionId);
        Vector3 world = GridManager.Instance.GridToWorldPosition(anchor);

        if (definition != null && definition.RefundAmount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(
                definition.RequiredResource,
                world,
                definition.RefundAmount);
            GameEvents.TriggerFloatingTextRequested(
                $"Reembolso: +{definition.RefundAmount} {definition.RequiredResource}",
                world,
                Color.green);
        }

        structure.ShutdownBehaviours();
        activeConfiguredStructures.Remove(anchor);
        GridManager.Instance.UnregisterFootprint(structure);
        GridManager.Instance.SetTileType(anchor.x, anchor.y, TileType.Empty, GridLayer.Structure);
        Destroy(structure.gameObject);
        StockpileManager.Instance?.RequestRefresh();
        GameEvents.TriggerFloatingTextRequested("Estrutura Desmontada", world, Color.yellow);
    }

    private static StructureFootprintDefinition ResolveFootprint(
        Vector2Int position,
        TileType fallbackType)
    {
        string id = GridManager.Instance?.GetContentId(
            position.x,
            position.y,
            GridLayer.Structure);
        BuildDefinitionSO definition = BuildCatalogService.Instance?.GetById(id);
        return definition != null
            ? definition.GetFootprint()
            : StructureFootprintSettings.Resolve(fallbackType);
    }

    private void DismantlePrintingPod(
        Vector2Int position,
        PrintingPod printingPod)
    {
        Vector3 spawnPosition =
            GridManager.Instance.GridToWorldPosition(position);

        printingPod.SetOperational(false);

        ResolveConstructionRefund(
            position,
            TileType.PrintingPod,
            out ResourceType resource,
            out int refundAmount);

        if (refundAmount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(
                resource,
                spawnPosition,
                refundAmount
            );

            GameEvents.TriggerFloatingTextRequested(
                $"Reembolso: +{refundAmount} {resource}",
                spawnPosition,
                Color.green);
        }

        ConfiguredStructure configured = printingPod.GetComponent<ConfiguredStructure>();
        UnregisterPrintingPod(position);
        if (configured != null)
        {
            activeConfiguredStructures.Remove(position);
            GridManager.Instance.UnregisterFootprint(configured);
        }
        GridManager.Instance.SetTileType(
            position.x,
            position.y,
            TileType.Empty
        );
        Destroy(printingPod.gameObject);

        StockpileManager.Instance?.RequestRefresh();
        GameEvents.TriggerFloatingTextRequested(
            "Máquina desmontada",
            spawnPosition,
            Color.yellow
        );
    }

    /// <summary>
    /// Usa a definição associada à célula. BuildingCosts permanece apenas
    /// como compatibilidade para construções antigas que não possuem ID.
    /// </summary>
    private static void ResolveConstructionRefund(
        Vector2Int position,
        TileType legacyTileType,
        out ResourceType resource,
        out int refundAmount)
    {
        string definitionId = GridManager.Instance?.GetContentId(
            position.x,
            position.y,
            GridLayer.Structure);

        if (string.IsNullOrWhiteSpace(definitionId))
        {
            definitionId = GridManager.Instance?.GetContentId(
                position.x,
                position.y,
                GridLayer.Terrain);
        }

        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(definitionId);

        if (definition != null)
        {
            resource = definition.RequiredResource;
            refundAmount = definition.RefundAmount;
            return;
        }

        resource = BuildingCosts.GetRequiredResource(legacyTileType);
        refundAmount = BuildingCosts.GetRefundAmount(legacyTileType);
    }
}
