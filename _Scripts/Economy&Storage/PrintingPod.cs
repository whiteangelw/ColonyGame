using System.Collections;
using UnityEngine;

public class PrintingPod : MonoBehaviour
{
    public static PrintingPod Instance { get; private set; }

    [Header("Configurações do Spawn")]
    [SerializeField] private GameObject duplicantPrefab;
    [SerializeField] private float cooldownTime = 10f;
    [SerializeField] private int maxPrintsAllowed = 5; // Limita o número máximo de cargas salvas na RAM

    [Header("Estado")]
    public float timeRemaining;
    public int availablePrints = 0;

    public Vector2Int gridPosition;
    private GridManager gridManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        UpdateGridPosition();

        timeRemaining = cooldownTime;
        StartCoroutine(CooldownRoutine());
    }

    public void UpdateGridPosition()
    {
        if (gridManager == null) gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        if (gridManager != null)
        {
            gridPosition = gridManager.WorldToGridPosition(transform.position);
        }
    }

    private IEnumerator CooldownRoutine()
    {
        WaitForSeconds waitFrame = new WaitForSeconds(0.1f);

        while (true)
        {
            if (availablePrints >= maxPrintsAllowed)
            {
                // Para de rodar a contagem caso o limite de cargas na RAM tenha sido atingido
                yield return waitFrame;
                continue;
            }

            timeRemaining = cooldownTime;

            while (timeRemaining > 0)
            {
                timeRemaining -= Time.deltaTime;
                yield return null;
            }

            timeRemaining = 0;
            availablePrints++;
        }
    }

    public void PrintDuplicant()
    {
        if (availablePrints <= 0) return;
        if (duplicantPrefab == null) return;

        if (gridManager == null) UpdateGridPosition();

        Vector3 spawnPos = transform.position;
        if (gridManager != null)
        {
            spawnPos = new Vector3(
                gridPosition.x * gridManager.cellSize + gridManager.cellSize / 2f,
                gridPosition.y * gridManager.cellSize + gridManager.cellSize / 2f,
                0f
            );
        }

        GameObject newDuplicant = Instantiate(duplicantPrefab, spawnPos, Quaternion.identity);

        if (newDuplicant != null)
        {
            availablePrints--;
        }
    }
}