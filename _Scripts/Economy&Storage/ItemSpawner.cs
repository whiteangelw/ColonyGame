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
        StockpileManager.Instance?.RequestRefresh();
        return true;
    }

    public ResourceItem RestoreResource(
        ResourceType type,
        Vector3 worldPosition,
        int amount)
    {
        if (ItemPoolManager.Instance == null || amount <= 0)
        {
            return null;
        }

        ResourceItem item = ItemPoolManager.Instance.Get(
            worldPosition,
            Quaternion.identity
        );

        if (item == null)
        {
            return null;
        }

        Sprite icon = null;

        if (itemDatabase != null)
        {
            ItemDataSO data = itemDatabase.GetItem(type);
            icon = data != null ? data.icon : null;
        }

        item.InitializeRestored(type, icon, amount);
        return item;
    }
}
