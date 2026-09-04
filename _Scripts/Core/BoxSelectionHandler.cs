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
        Vector3 mouseScreenPos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0;
        Vector2Int currentGridPos = GridManager.Instance.WorldToGridPosition(mouseWorldPos);

        // --- CLIQUE ESQUERDO ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // 1. Tenta interagir primeiro com o Baú no Mundo (Física 2D)
            Collider2D hit = Physics2D.OverlapPoint(mouseWorldPos);
            if (hit != null && hit.TryGetComponent<StorageStructure>(out var storageByPhysics))
            {
                storageByPhysics.OnInteract();
                return;
            }

            // 2. Tenta interagir com o Baú via Dicionário do Grid
            StorageStructure storageByGrid = StructureManager.Instance?.GetStorageAt(currentGridPos);
            if (storageByGrid != null)
            {
                storageByGrid.OnInteract();
                return;
            }

            // 3. Se o clique FOI sobre um elemento de UI (botão, painel, etc.), encerra aqui sem criar caixa de seleção
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            // 4. Lógica do modo Haul (Transporte)
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

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = GridManager.Instance.GetTile(x, y);
                if (tile == null) continue;

                if (playerInput.currentMode == InputMode.Dig)
                {
                    if (tile.type != TileType.Empty && tile.type != TileType.Bedrock &&
                        tile.type != TileType.Chest && tile.type != TileType.Ladder &&
                        tile.type != TileType.PrintingPod)
                    {
                        TaskManager.Instance?.AddTask(new Vector2Int(x, y), TaskType.Dig);
                    }
                }
                else if (playerInput.currentMode == InputMode.Dismantle)
                {
                    if (tile.type == TileType.Chest
                        || tile.type == TileType.Ladder
                        || tile.type == TileType.PrintingPod)
                    {
                        WorldInteractionService.Instance?.DismantleTile(x, y);
                    }
                }
            }
        }
    }

    private void ProcessBuildSelection(int minX, int maxX, int minY, int maxY)
    {
        TileType buildTile = playerInput.selectedBuildTile;

        if (buildTile == TileType.PrintingPod)
        {
            ProcessSingleMachineSelection(minX, maxX, minY, maxY);
            return;
        }

        ResourceType reqResource = BuildingCosts.GetRequiredResource(buildTile);
        int costPerTile = BuildingCosts.GetCost(buildTile);

        int validTilesCount = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = GridManager.Instance.GetTile(x, y);
                // Valida se o tile está livre e não possui outro blueprint ativo
                if (tile != null && tile.type == TileType.Empty && TaskManager.Instance?.GetTaskAt(new Vector2Int(x, y)) == null)
                {
                    validTilesCount++;
                }
            }
        }

        if (validTilesCount == 0) return;

        int totalCost = validTilesCount * costPerTile;
        bool hasEnoughResources = StockpileManager.Instance == null || StockpileManager.Instance.HasResource(reqResource, totalCost);

        if (!hasEnoughResources)
        {
            float cs = GridManager.Instance.cellSize;
            Vector3 centerPos = new Vector3(((minX + maxX + 1) * cs) / 2f, ((minY + maxY + 1) * cs) / 2f, 0);
            FloatingTextManager.Instance?.ShowText("Sem Recursos Suficientes no Mundo!", centerPos, Color.red);
            return;
        }

        // Instancia a entidade de Blueprint para cada tile selecionado
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = GridManager.Instance.GetTile(x, y);
                Vector2Int pos = new Vector2Int(x, y);

                if (tile != null && tile.type == TileType.Empty && TaskManager.Instance?.GetTaskAt(pos) == null)
                {
                    BlueprintManager.Instance?.CreateBlueprint(pos, buildTile);
                }
            }
        }
    }

    private void ProcessSingleMachineSelection(
        int minX,
        int maxX,
        int minY,
        int maxY)
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

        if (!BlueprintManager.Instance.CanCreateBlueprint(
                TileType.PrintingPod))
        {
            ShowMachinePlacementMessage(
                "Já existe um Printing Pod ou um projeto dele.",
                minX,
                maxX,
                minY,
                maxY
            );
            return;
        }

        ResourceType resource = BuildingCosts.GetRequiredResource(
            TileType.PrintingPod
        );
        int cost = BuildingCosts.GetCost(TileType.PrintingPod);

        if (StockpileManager.Instance != null
            && !StockpileManager.Instance.HasResource(resource, cost))
        {
            ShowMachinePlacementMessage(
                $"Sem recursos suficientes! Necessário: {cost} {resource}.",
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
                Tile tile = GridManager.Instance.GetTile(position);

                if (tile == null
                    || tile.type != TileType.Empty
                    || TaskManager.Instance?.GetTaskAt(position) != null)
                {
                    continue;
                }

                ConstructionBlueprint blueprint =
                    BlueprintManager.Instance.CreateBlueprint(
                    position,
                    TileType.PrintingPod
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

        ShowMachinePlacementMessage(
            "Escolha uma célula vazia e sem outra tarefa.",
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
