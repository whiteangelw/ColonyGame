using UnityEngine;

/// <summary>
/// Base genérica para criar Singletons persistentes ou de cena com acesso global seguro.
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    private static readonly object lockObject = new object();
    private static bool isQuitting = false;

    public static T Instance
    {
        get
        {
            if (isQuitting)
            {
                return null;
            }

            lock (lockObject)
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<T>();

                    if (instance == null && !isQuitting)
                    {
                        GameObject singletonObject = new GameObject(typeof(T).Name + " (Auto-Generated)");
                        instance = singletonObject.AddComponent<T>();
                    }
                }
                return instance;
            }
        }
    }

    protected virtual void Awake()
    {
        // Reseta a trava ao iniciar o jogo no Editor
        isQuitting = false;

        if (instance == null)
        {
            instance = this as T;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnApplicationQuit()
    {
        isQuitting = true;
    }

    protected virtual void OnDestroy()
    {
        if (instance == this)
        {
            isQuitting = true;
        }
    }
}