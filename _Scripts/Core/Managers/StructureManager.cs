using System.Collections.Generic;
using UnityEngine;

public class StructureManager : Singleton<StructureManager>
{
    [Header("Prefabs de Estruturas")]
    [SerializeField] private GameObject chestPrefab;
    [SerializeField] private GameObject printingPodPrefab;

    private readonly Dictionary<Vector2Int, StorageStructure> activeStorages = new Dictionary<Vector2Int, StorageStructure>();
    private readonly Dictionary<Vector2Int, PrintingPod> activePrintingPods =
        new Dictionary<Vector2Int, PrintingPod>();

    public PrintingPod GetPrintingPodAt(Vector2Int position)
    {
        activePrintingPods.TryGetValue(position, out PrintingPod printingPod);
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
    }

    public void UnregisterPrintingPod(Vector2Int position)
    {
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
        QueueExistingGroundItems();
    }

    public void UnregisterStorage(Vector2Int pos)
    {
        if (activeStorages.ContainsKey(pos))
        {
            activeStorages.Remove(pos);
        }
    }

    public StorageStructure GetStorageAt(Vector2Int position)
    {
        activeStorages.TryGetValue(position, out var storage);
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
        foreach (StorageStructure storage in activeStorages.Values)
        {
            if (storage != null)
            {
                storage.gameObject.SetActive(false);
                Destroy(storage.gameObject);
            }
        }

        foreach (PrintingPod printingPod in activePrintingPods.Values)
        {
            if (printingPod != null)
            {
                printingPod.SetOperational(false);
                printingPod.gameObject.SetActive(false);
                Destroy(printingPod.gameObject);
            }
        }

        activeStorages.Clear();
        activePrintingPods.Clear();
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
            DismantlePrintingPod(position, printingPod);
            return;
        }

        StorageStructure storage = GetStorageAt(position);

        // A estrutura já foi marcada quando a tarefa foi criada.
        if (storage == null) return;

        if (!storage.IsBeingDismantled)
        {
            storage.MarkAsDismantled();
        }

        float cs = GridManager.Instance.cellSize;
        Vector3 spawnPos = new Vector3(position.x * cs + cs / 2f, position.y * cs + cs / 2f, 0);

        // 2. DROPA OS ITENS QUE ESTAVAM DENTRO DO BAÚ
        var storedItems = storage.DrainForDismantle();
        foreach (var kvp in storedItems)
        {
            if (kvp.Value > 0)
            {
                ItemSpawner.Instance?.SpawnResource(kvp.Key, spawnPos, kvp.Value);
            }
        }

        // 3. REEMBOLSA O CUSTO DE CONSTRUÇÃO (Ex: 5 Cobres)
        ResourceType reqResource = BuildingCosts.GetRequiredResource(TileType.Chest);
        int refundAmount = BuildingCosts.GetRefundAmount(TileType.Chest);

        if (refundAmount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(reqResource, spawnPos, refundAmount);
        }

        // 4. DESREGISTRA E LIMPA A CÉLULA NO GRID
        UnregisterStorage(position);
        GridManager.Instance.SetTileType(position.x, position.y, TileType.Empty);

        // 5. DESTRÓI O GAMEOBJECT FÍSICO
        Destroy(storage.gameObject);

        StockpileManager.Instance?.RefreshReachableResources();

        // 6. FEEDBACK VISUAL
        GameEvents.TriggerFloatingTextRequested("Estrutura Desmontada", spawnPos, Color.yellow);
    }

    private void DismantlePrintingPod(
        Vector2Int position,
        PrintingPod printingPod)
    {
        Vector3 spawnPosition =
            GridManager.Instance.GridToWorldPosition(position);

        printingPod.SetOperational(false);

        ResourceType resource = BuildingCosts.GetRequiredResource(
            TileType.PrintingPod
        );
        int refundAmount = BuildingCosts.GetRefundAmount(
            TileType.PrintingPod
        );

        if (refundAmount > 0)
        {
            ItemSpawner.Instance?.SpawnResource(
                resource,
                spawnPosition,
                refundAmount
            );
        }

        UnregisterPrintingPod(position);
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
}
