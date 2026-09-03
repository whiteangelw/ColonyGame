using UnityEngine;

public class FloatingTextManager : Singleton<FloatingTextManager>
{
    [Header("Configuração da Pool")]
    [SerializeField] private FloatingText floatingTextPrefab;
    [SerializeField] private int initialPoolSize = 10;

    private System.Collections.Generic.Queue<FloatingText> textPool = new System.Collections.Generic.Queue<FloatingText>();

    protected override void Awake()
    {
        base.Awake();
        InitializePool();
    }

    private void OnEnable()
    {
        GameEvents.OnFloatingTextRequested += ShowText;
    }

    private void OnDisable()
    {
        GameEvents.OnFloatingTextRequested -= ShowText;
    }

    private void InitializePool()
    {
        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[FloatingTextManager] Atenção: Prefab do FloatingText não foi atribuído no Inspector!");
            return;
        }

        for (int i = 0; i < initialPoolSize; i++)
        {
            FloatingText obj = Instantiate(floatingTextPrefab, transform);
            obj.gameObject.SetActive(false);
            textPool.Enqueue(obj);
        }
    }

    public void ShowText(string message, Vector3 worldPosition, Color color)
    {
        if (floatingTextPrefab == null && textPool.Count == 0)
        {
            Debug.LogWarning("[FloatingTextManager] Não foi possível exibir o texto: Prefab está ausente no Inspector!");
            return;
        }

        Vector3 spawnPos = worldPosition + new Vector3(0, 0.5f, 0);
        FloatingText textObj;

        if (textPool.Count > 0)
        {
            textObj = textPool.Dequeue();
            textObj.transform.position = spawnPos;
            textObj.transform.rotation = Quaternion.identity;
            textObj.gameObject.SetActive(true);
        }
        else
        {
            textObj = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity, transform);
        }

        if (textObj != null)
        {
            textObj.Setup(message, color);
        }
    }

    public void ReturnToPool(FloatingText textObj)
    {
        if (textObj == null) return;

        textObj.gameObject.SetActive(false);
        textObj.transform.SetParent(transform);
        textPool.Enqueue(textObj);
    }
}