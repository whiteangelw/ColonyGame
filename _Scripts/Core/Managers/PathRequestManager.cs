using System;
using System.Collections.Generic;
using UnityEngine;

public class PathRequestManager : MonoBehaviour
{
    public static PathRequestManager Instance { get; private set; }

    [Header("Configuração de Time-Slicing")]
    [SerializeField] private int maxPathsPerFrame = 3;

    private Queue<PathRequest> pathRequestQueue = new Queue<PathRequest>();

    struct PathRequest
    {
        public Vector2Int start;
        public Vector2Int target;
        public Action<List<Vector2Int>> callback;

        public PathRequest(Vector2Int _start, Vector2Int _target, Action<List<Vector2Int>> _callback)
        {
            start = _start;
            target = _target;
            callback = _callback;
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public static void RequestPath(Vector2Int start, Vector2Int target, Action<List<Vector2Int>> callback)
    {
        if (Instance == null) return;
        PathRequest newRequest = new PathRequest(start, target, callback);
        Instance.pathRequestQueue.Enqueue(newRequest);
    }

    private void Update()
    {
        int processedThisFrame = 0;

        while (pathRequestQueue.Count > 0 && processedThisFrame < maxPathsPerFrame)
        {
            PathRequest request = pathRequestQueue.Dequeue();
            List<Vector2Int> path = PathfindingAStar.Instance != null ?
                PathfindingAStar.Instance.FindPath(request.start, request.target) : null;

            request.callback?.Invoke(path);
            processedThisFrame++;
        }
    }
}