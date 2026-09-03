using UnityEngine;

public class ItemSpawner : Singleton<ItemSpawner>
{
    [Header("Banco de Dados de Itens")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    protected override void Awake()
    {
        base.Awake();
        if (itemDatabase != null)
        {
            itemDatabase.Initialize();
        }
        else
        {
            Debug.LogWarning("[ItemSpawner] ItemDatabaseSO não foi atribuído no Inspector!");
        }
    }

    public void SpawnResource(ResourceType type, Vector3 worldPosition, int overrideAmount = -1)
    {
        TrySpawnResource(type, worldPosition, overrideAmount);
    }

    public bool TrySpawnResource(
        ResourceType type,
        Vector3 worldPosition,
        int overrideAmount = -1)
    {
        if (ItemPoolManager.Instance == null)
        {
            Debug.LogError("[ItemSpawner] Erro: ItemPoolManager não encontrado na cena!");
            return false;
        }

        // Pega o ResourceItem direto da Pool de Objetos
        ResourceItem itemComponent = ItemPoolManager.Instance.Get(worldPosition, Quaternion.identity);

        if (itemComponent == null)
        {
            Debug.LogError("[ItemSpawner] A pool não conseguiu fornecer um ResourceItem.");
            return false;
        }

        Sprite icon = null;
        int amount = overrideAmount > 0 ? overrideAmount : 1;

        if (itemDatabase != null)
        {
            ItemDataSO data = itemDatabase.GetItem(type);
            if (data != null) icon = data.icon;
        }

        itemComponent.Initialize(type, icon, amount);
        TaskManager.Instance?.AddHaulTask(itemComponent);
        StockpileManager.Instance?.RequestRefresh();
        return true;
    }
}
