using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using TMPro;

public class TaskOverlayVisualizer : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Tilemap overlayTilemap;
    [SerializeField] private GameObject priorityNumberPrefab; // Prefab contendo TextMeshPro

    [Header("Assets de Marcação de Tarefa")]
    [SerializeField] private TileBase digTaskTileAsset;
    [SerializeField] private TileBase buildLadderTaskTileAsset;
    [SerializeField] private TileBase buildTileTaskTileAsset;

    // Pool de objetos para reutilizar os textos de prioridade sem travar a memória
    private Dictionary<Vector2Int, GameObject> activePriorityTexts = new Dictionary<Vector2Int, GameObject>();
    private Queue<GameObject> textPool = new Queue<GameObject>();

    private void Start()
    {
        if (overlayTilemap == null)
            overlayTilemap = GetComponent<Tilemap>();

        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTaskAdded += OnTaskAddedHandler;
            TaskManager.Instance.OnTaskRemoved += OnTaskRemovedHandler;
        }
    }

    private void OnDestroy()
    {
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTaskAdded -= OnTaskAddedHandler;
            TaskManager.Instance.OnTaskRemoved -= OnTaskRemovedHandler;
        }
    }

    private void OnTaskAddedHandler(Task task)
    {
        Vector3Int tilePosition = new Vector3Int(task.gridPosition.x, task.gridPosition.y, 0);

        // 1. Desenha o Tile de Overlay diretamente no Tilemap (Batching nativo)
        if (task.type == TaskType.Dig && digTaskTileAsset != null)
        {
            overlayTilemap.SetTile(tilePosition, digTaskTileAsset);
        }
        else if (task.type == TaskType.BuildTile)
        {
            if (task.buildTileType == TileType.Ladder && buildLadderTaskTileAsset != null)
                overlayTilemap.SetTile(tilePosition, buildLadderTaskTileAsset);
            else if (buildTileTaskTileAsset != null)
                overlayTilemap.SetTile(tilePosition, buildTileTaskTileAsset);
        }

        // 2. Gerencia o texto usando Object Pooling
        if (priorityNumberPrefab != null && !activePriorityTexts.ContainsKey(task.gridPosition))
        {
            Vector3 worldPos = overlayTilemap.GetCellCenterWorld(tilePosition);

            // Reutiliza objeto inativo ou instancia um novo se a pool estiver vazia
            GameObject textObj = GetPriorityTextObject(worldPos);

            TMP_Text textMesh = textObj.GetComponent<TMP_Text>();
            if (textMesh != null)
            {
                textMesh.text = task.priority.ToString();
            }

            activePriorityTexts.Add(task.gridPosition, textObj);
        }
    }

    private void OnTaskRemovedHandler(Task task)
    {
        Vector3Int tilePosition = new Vector3Int(task.gridPosition.x, task.gridPosition.y, 0);

        // Remove o Tile do Overlay
        overlayTilemap.SetTile(tilePosition, null);

        // Devolve o Texto para a Pool em vez de destruí-lo
        if (activePriorityTexts.TryGetValue(task.gridPosition, out GameObject textObj))
        {
            textObj.SetActive(false);
            textPool.Enqueue(textObj);
            activePriorityTexts.Remove(task.gridPosition);
        }
    }

    private GameObject GetPriorityTextObject(Vector3 position)
    {
        GameObject textObj;
        if (textPool.Count > 0)
        {
            textObj = textPool.Dequeue();
            textObj.transform.position = position;
            textObj.SetActive(true);
        }
        else
        {
            textObj = Instantiate(priorityNumberPrefab, position, Quaternion.identity, transform);
        }
        return textObj;
    }

    public void UpdateTaskPriorityText(Vector2Int gridPos, int newPriority)
    {
        if (activePriorityTexts.TryGetValue(gridPos, out GameObject textObj))
        {
            TMP_Text textMesh = textObj.GetComponent<TMP_Text>();
            if (textMesh != null)
            {
                textMesh.text = newPriority.ToString();
            }
        }
    }
}