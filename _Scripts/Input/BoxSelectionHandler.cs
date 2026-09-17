using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

[RequireComponent(typeof(PlayerInput))]
public class BoxSelectionHandler : MonoBehaviour
{
    private Camera mainCamera;
    private PlayerInput playerInput;

    private bool isDraggingLeft = false;
    private Vector2Int dragStartGridPos;
    private Vector2Int dragEndGridPos;

    // Frente para trás: cliques repetidos alcançam as camadas inferiores.
    private static readonly GridLayer[] dismantleLayerOrder =
    {
        GridLayer.Decoration,
        GridLayer.Structure,
        GridLayer.BackWall,
        GridLayer.Terrain
    };

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
    }

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (GridManager.Instance == null || mainCamera == null) return;

        HandleDragSelection();
    }

    private void HandleDragSelection()
    {
        bool isTextEditing = InputContextService.Instance != null
            && InputContextService.Instance.IsTextEditing;

        Vector3 mouseScreenPos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0;
        Vector2Int currentGridPos = GridManager.Instance.WorldToGridPosition(mouseWorldPos);

        // --- CLIQUE ESQUERDO ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            bool pointerIsOverUI = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();

            if (isTextEditing && pointerIsOverUI)
            {
                isDraggingLeft = false;
                return;
            }

            // Cancelar e desmontar pertencem à ferramenta, não à UI da estrutura.
            if (playerInput.currentMode == InputMode.Cancel
                || playerInput.currentMode == InputMode.Dismantle)
            {
                if (pointerIsOverUI) return;

                isDraggingLeft = true;
                dragStartGridPos = currentGridPos;
                dragEndGridPos = currentGridPos;
                return;
            }

            // Interações pontuais do mundo têm prioridade sobre as ferramentas.
            // OverlapPointAll evita depender da ordem interna da física quando
            // personagem, decoração ou outro trigger ocupa a mesma célula.
            Collider2D[] pointHits = Physics2D.OverlapPointAll(mouseWorldPos);
            foreach (Collider2D pointHit in pointHits)
            {
                if (pointHit == null) continue;

                StorageStructure storageByPhysics =
                    pointHit.GetComponentInParent<StorageStructure>();

                if (storageByPhysics != null)
                {
                    storageByPhysics.OnInteract();
                    return;
                }
            }

            // Fallback determinístico pelo grid, inclusive para footprints maiores.
            StorageStructure storageByGrid = StructureManager.Instance?.GetStorageAt(currentGridPos);
            if (storageByGrid != null)
            {
                storageByGrid.OnInteract();
                return;
            }

            // A UI ainda bloqueia seleção em caixa e criação de tarefas, mas não
            // impede que uma estrutura explicitamente clicada abra sua inspeção.
            if (pointerIsOverUI) return;

            // Lógica do modo Haul (Transporte)
            if (playerInput.currentMode == InputMode.Haul)
            {
                Vector2 mouseWorldPos2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
                Collider2D[] hitColliders = Physics2D.OverlapCircleAll(mouseWorldPos2D, 0.8f);

                foreach (var col in hitColliders)
                {
                    if (col.TryGetComponent<ResourceItem>(out var item))
                    {
                        TaskManager.Instance?.AddHaulTask(item);
                        return;
                    }
                }
            }

            // 5. Se clicou no mapa vazio, inicia seleção em caixa
            isDraggingLeft = true;
            dragStartGridPos = currentGridPos;
            dragEndGridPos = currentGridPos;
        }

        if (isDraggingLeft && Mouse.current.leftButton.isPressed)
        {
            dragEndGridPos = currentGridPos;
        }

        if (isDraggingLeft && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDraggingLeft = false;

            if (playerInput.currentMode == InputMode.Haul)
            {
                CollectItemsInBox();
            }
            else
            {
                ApplyBoxSelectionTasks();
            }
        }

        // --- BOTÃO DIREITO ---
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            playerInput.CloseBuildMenu();
            isDraggingLeft = false;
        }
    }

    private void CollectItemsInBox()
    {
        if (GridManager.Instance == null) return;

        GetGridBounds(out int minX, out int maxX, out int minY, out int maxY);

        float cs = GridManager.Instance.cellSize;
        Vector2 center = new Vector2(((minX + maxX + 1) * cs) / 2f, ((minY + maxY + 1) * cs) / 2f);
        Vector2 size = new Vector2((maxX - minX + 1) * cs, (maxY - minY + 1) * cs);

        Collider2D[] itemsInArea = Physics2D.OverlapBoxAll(center, size, 0f);
        foreach (var col in itemsInArea)
        {
            if (col.TryGetComponent<ResourceItem>(out var item))
            {
                TaskManager.Instance?.AddHaulTask(item);
            }
        }
    }

    private void ApplyBoxSelectionTasks()
    {
        if (GridManager.Instance == null || playerInput == null) return;

        GetGridBounds(out int minX, out int maxX, out int minY, out int maxY);

        if (playerInput.currentMode == InputMode.Build)
        {
            ProcessBuildSelection(minX, maxX, minY, maxY);
            return;
        }

        if (playerInput.currentMode == InputMode.Cancel)
        {
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int position = new Vector2Int(x, y);
                // Blueprint primeiro: seu OnDestroy também remove a tarefa e devolve materiais.
                bool removedBlueprint = BlueprintManager.Instance?.CancelBlueprintAt(position) ?? false;
                if (!removedBlueprint)
                    TaskManager.Instance?.CancelTasksAt(position);
            }
            return;
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                if (playerInput.currentMode == InputMode.Harvest)
                {
                    FloraEntity flora = FloraManager.Instance?.GetFloraAt(
                        position);
                    if (flora != null)
                    {
                        TaskManager.Instance?.AddHarvestTask(flora);
                    }
                    continue;
                }

                Tile tile = GridManager.Instance.GetTile(x, y);
                if (tile == null) continue;

                if (playerInput.currentMode == InputMode.Dig)
                {
                    if (tile.type != TileType.Empty && tile.type != TileType.Bedrock &&
                        tile.type != TileType.Chest && tile.type != TileType.Ladder &&
                        tile.type != TileType.PrintingPod)
                    {
                        TaskManager.Instance?.AddTask(position, TaskType.Dig);
                    }
                }
                else if (playerInput.currentMode == InputMode.Dismantle)
                {
                    QueueNextDismantleLayer(position);
                }
            }
        }
    }

    private void QueueNextDismantleLayer(Vector2Int position)
    {
        WorldInteractionService interaction = WorldInteractionService.Instance;
        if (interaction == null) return;

        foreach (GridLayer layer in dismantleLayerOrder)
        {
            bool taskAlreadyQueued = TaskManager.Instance != null
                && TaskManager.Instance.GetTaskAt(
                    position,
                    TaskType.Dismantle,
                    layer) != null;

            if (taskAlreadyQueued
                || !interaction.CanDismantleAt(position, layer))
            {
                continue;
            }

            bool taskCreated = interaction.DismantleTile(
                position.x,
                position.y,
                layer);

            if (!taskCreated) continue;

            GameEvents.TriggerFloatingTextRequested(
                $"Desmontar: {GetLayerDisplayName(layer)}",
                GridManager.Instance.GridToWorldPosition(position),
                Color.yellow);
            return;
        }
    }

    private static string GetLayerDisplayName(GridLayer layer)
    {
        switch (layer)
        {
            case GridLayer.Decoration:
                return "Decoração";
            case GridLayer.Structure:
                return "Estrutura";
            case GridLayer.BackWall:
                return "Parede";
            default:
                return "Terreno";
        }
    }

    private void ProcessBuildSelection(int minX, int maxX, int minY, int maxY)
    {
        TileType buildTile = playerInput.selectedBuildTile;
        GridLayer buildLayer = playerInput.selectedBuildLayer;
        string definitionId = playerInput.selectedBuildDefinitionId;
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(definitionId);
        StructureFootprintDefinition footprint = definition != null
            ? definition.GetFootprint()
            : StructureFootprintSettings.Resolve(buildTile);

        if (footprint.IsMultiCell)
        {
            ProcessSingleStructureSelection(
                minX,
                maxX,
                minY,
                maxY,
                buildTile,
                buildLayer,
                definitionId);
            return;
        }

        int validTilesCount = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int position = new Vector2Int(x, y);
                if (BlueprintManager.Instance != null
                    && BlueprintManager.Instance.CanPlanBuildHere(
                        position,
                        definitionId,
                        buildTile,
                        buildLayer,
                        out _))
                {
                    validTilesCount++;
                }
            }
        }

        if (validTilesCount == 0) return;

        // Instancia a entidade de Blueprint para cada tile selecionado
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);

                if (BlueprintManager.Instance != null
                    && BlueprintManager.Instance.CanPlanBuildHere(
                        pos,
                        definitionId,
                        buildTile,
                        buildLayer,
                        out _))
                {
                    BlueprintManager.Instance?.CreateBlueprint(
                        pos,
                        definitionId,
                        buildTile,
                        buildLayer);
                }
            }
        }
    }

    private void ProcessSingleStructureSelection(
        int minX,
        int maxX,
        int minY,
        int maxY,
        TileType buildTile,
        GridLayer buildLayer,
        string definitionId)
    {
        if (BlueprintManager.Instance == null)
        {
            ShowMachinePlacementMessage(
                "BlueprintManager não encontrado.",
                minX,
                maxX,
                minY,
                maxY
            );
            return;
        }

        if (!BlueprintManager.Instance.CanCreateBlueprint(buildTile))
        {
            ShowMachinePlacementMessage(
                "Esta estrutura única já existe ou possui um blueprint.",
                minX,
                maxX,
                minY,
                maxY
            );
            return;
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int position = new Vector2Int(x, y);
                if (!BlueprintManager.Instance.CanPlanBuildHere(
                        position,
                        definitionId,
                        buildTile,
                        buildLayer,
                        out _))
                {
                    continue;
                }

                ConstructionBlueprint blueprint =
                    BlueprintManager.Instance.CreateBlueprint(
                        position,
                        definitionId,
                        buildTile,
                        buildLayer
                );

                if (blueprint == null)
                {
                    ShowMachinePlacementMessage(
                        "Falha ao criar o blueprint. Verifique o Console.",
                        minX,
                        maxX,
                        minY,
                        maxY
                    );
                }

                return;
            }
        }

        Vector2Int selectedPosition = new Vector2Int(minX, minY);
        BlueprintManager.Instance.CanPlanBuildHere(
            selectedPosition,
            definitionId,
            buildTile,
            buildLayer,
            out string failureReason);

        ShowMachinePlacementMessage(
            string.IsNullOrWhiteSpace(failureReason)
                ? "Escolha uma área livre e com apoio suficiente."
                : failureReason,
            minX,
            maxX,
            minY,
            maxY
        );
    }

    private void ShowMachinePlacementMessage(
        string message,
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogWarning(message);
            return;
        }

        float cellSize = GridManager.Instance.cellSize;
        Vector3 centerPosition = new Vector3(
            (minX + maxX + 1) * cellSize * 0.5f,
            (minY + maxY + 1) * cellSize * 0.5f,
            0f
        );

        GameEvents.TriggerFloatingTextRequested(
            message,
            centerPosition,
            Color.red
        );

        Debug.LogWarning("[Build] " + message);
    }

    private void GetGridBounds(out int minX, out int maxX, out int minY, out int maxY)
    {
        minX = Mathf.Min(dragStartGridPos.x, dragEndGridPos.x);
        maxX = Mathf.Max(dragStartGridPos.x, dragEndGridPos.x);
        minY = Mathf.Min(dragStartGridPos.y, dragEndGridPos.y);
        maxY = Mathf.Max(dragStartGridPos.y, dragEndGridPos.y);
    }
}
