using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pool de Objetos Genérica para qualquer Componente/MonoBehaviour.
/// </summary>
public abstract class GenericObjectPool<T> : Singleton<GenericObjectPool<T>> where T : Component
{
    [Header("Configurações da Pool")]
    [SerializeField] private T prefab;
    [SerializeField] private int initialPoolSize = 30;

    private readonly Queue<T> poolQueue = new Queue<T>();
    private readonly HashSet<T> pooledObjects = new HashSet<T>();

    protected override void Awake()
    {
        base.Awake();
        InitializePool();
    }

    private void InitializePool()
    {
        if (prefab == null) return;

        for (int i = 0; i < initialPoolSize; i++)
        {
            T obj = CreateNewInstance();
            obj.gameObject.SetActive(false);
            poolQueue.Enqueue(obj);
            pooledObjects.Add(obj);
        }
    }

    private T CreateNewInstance()
    {
        T instance = Instantiate(prefab, transform);
        return instance;
    }

    public T Get(Vector3 position, Quaternion rotation)
    {
        T obj;

        if (poolQueue.Count > 0)
        {
            obj = poolQueue.Dequeue();
            pooledObjects.Remove(obj);
        }
        else
        {
            if (prefab == null)
            {
                Debug.LogError($"[{GetType().Name}] Prefab da pool não configurado.");
                return null;
            }

            obj = CreateNewInstance();
        }

        obj.transform.SetParent(null);
        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.gameObject.SetActive(true);
        return obj;
    }

    public void ReturnToPool(T obj)
    {
        if (obj == null || !pooledObjects.Add(obj))
        {
            return;
        }

        obj.gameObject.SetActive(false);
        obj.transform.SetParent(transform);
        poolQueue.Enqueue(obj);
    }
}
