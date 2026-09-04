using System.Collections.Generic;
using UnityEngine;

public class ColonyBootstrap : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private DuplicantSpawnService spawnService;
    [SerializeField] private CameraController cameraController;

    [Header("Colônia inicial")]
    [Min(1)]
    [SerializeField] private int initialDuplicantCount = 2;

    [Tooltip("Evita duplicar personagens deixados manualmente na cena.")]
    [SerializeField] private bool countExistingDuplicants = true;

    private GridManager gridManager;
    private bool initialized;

    private void Start()
    {
        gridManager = GridManager.Instance;

        if (gridManager == null)
        {
            Debug.LogError("[ColonyBootstrap] GridManager não encontrado.");
            return;
        }

        gridManager.OnGridRebuilt += HandleWorldReady;

        if (gridManager.IsGridReady)
        {
            HandleWorldReady();
        }
    }

    private void OnDestroy()
    {
        if (gridManager != null)
        {
            gridManager.OnGridRebuilt -= HandleWorldReady;
        }
    }

    private void HandleWorldReady()
    {
        if (initialized)
        {
            return;
        }

        WorldGenerator worldGenerator =
            gridManager.GetComponent<WorldGenerator>();

        if (worldGenerator == null || spawnService == null)
        {
            Debug.LogError(
                "[ColonyBootstrap] WorldGenerator ou SpawnService não configurado."
            );
            return;
        }

        initialized = true;

        int existingCount = countExistingDuplicants
            ? FindObjectsByType<DuplicantController>(
                FindObjectsSortMode.None
            ).Length
            : 0;

        int amountToSpawn = Mathf.Max(
            0,
            initialDuplicantCount - existingCount
        );

        spawnService.SpawnRandomGroup(
            worldGenerator.GetSpawnPosition(),
            amountToSpawn
        );

        FocusCamera();
    }

    private void FocusCamera()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }

        if (cameraController == null)
        {
            return;
        }

        DuplicantController[] allDuplicants =
            FindObjectsByType<DuplicantController>(
                FindObjectsSortMode.None
            );

        List<Transform> targets = new List<Transform>();

        for (int i = 0; i < allDuplicants.Length; i++)
        {
            if (allDuplicants[i] != null)
            {
                targets.Add(allDuplicants[i].transform);
            }
        }

        cameraController.FocusOnTargets(targets);
    }
}
