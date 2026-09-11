using System;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : Singleton<GridManager>, IGridService
{
    private bool suppressLegacyStructureSpawn;
    [Header("Configurações do Mapa")]
    public int width = 20;
    public int height = 15;
    public float cellSize = 1.0f;

    public bool IsGridReady { get; private set; }

    public int Width => width;
    public int Height => height;

    public event Action<int, int, TileType> OnTileChanged;
    public event Action<int, int> OnLiquidChanged;
    public event Action<int, int, GridLayer, TileType> OnLayerTileChanged;
    public event Action<GridOccupancyRecord, GridOccupancyChangeType>
        OnOccupancyChanged;
    public event Action OnGridRebuilt;

    private readonly GridData gridData = new GridData();
    private GridOccupancyService occupancyService;

    private GridOccupancyService Occupancy
    {
        get
        {
            if (occupancyService == null)
            {
                occupancyService = new GridOccupancyService(IsInsideGrid);
                occupancyService.OccupancyChanged += HandleOccupancyChanged;
            }

            return occupancyService;
        }
    }

    public IStorage GetAvailableStorageFor(ResourceType type, int amount)
    {
        return StructureManager.Instance?.GetAvailableStorageFor(type, amount);
    }

    private void Start()
    {
        if (SaveGameService.Instance != null
            && SaveGameService.Instance.TryBeginAutomaticLoad())
        {
            return;
        }

        GenerateGrid();
    }

    public void GenerateNewWorld()
    {
        if (IsGridReady)
        {
            return;
        }

        GenerateGrid();
    }

    private void GenerateGrid()
    {
        IsGridReady = false;
        ClearOccupancy();

        gridData.Create(width, height);
        width = gridData.Width;
        height = gridData.Height;

        WorldGenerator generator = GetComponent<WorldGenerator>();

        if (generator != null)
        {
            generator.GenerateWorld();
        }

        // Mantém as bordas do mapa protegidas.
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || x == width - 1 || y == 0)
                {
                    SetTileType(x, y, TileType.Bedrock);
                }
            }
        }

        IsGridReady = true;
        OnGridRebuilt?.Invoke();
    }

    public void BeginSnapshotRestore(int restoredWidth, int restoredHeight)
    {
        width = Mathf.Max(1, restoredWidth);
        height = Mathf.Max(1, restoredHeight);
        IsGridReady = false;
        ClearOccupancy();
        gridData.Create(width, height);
        width = gridData.Width;
        height = gridData.Height;
    }

    public void RestoreTileState(
        int x,
        int y,
        TileType type,
        FogState fogState,
        float liquidAmount,
        TileType backWallType = TileType.Empty,
        TileType decorationType = TileType.Empty,
        string terrainContentId = "",
        string structureContentId = "",
        string backWallContentId = "",
        string decorationContentId = "")
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return;
        }

        gridData.SetTileType(x, y, GridLayer.Terrain, type);
        gridData.SetTileType(x, y, GridLayer.BackWall, backWallType);
        gridData.SetTileType(x, y, GridLayer.Decoration, decorationType);
        gridData.SetContentId(x, y, GridLayer.Terrain, terrainContentId);
        gridData.SetContentId(x, y, GridLayer.Structure, structureContentId);
        gridData.SetContentId(x, y, GridLayer.BackWall, backWallContentId);
        gridData.SetContentId(x, y, GridLayer.Decoration, decorationContentId);
        tile.isPassable = !IsSolidTileType(type);
        tile.fogState = fogState;
        tile.liquidAmount = Mathf.Max(0f, liquidAmount);
        tile.reachabilityGroupID = -1;
    }

    public void CompleteSnapshotRestore(bool notifyListeners = true)
    {
        IsGridReady = true;
        if (notifyListeners)
        {
            PublishGridRebuilt();
        }
    }

    public void PublishGridRebuilt()
    {
        if (!IsGridReady)
        {
            return;
        }

        OnGridRebuilt?.Invoke();
    }

    public Tile GetTile(int x, int y)
    {
        // Durante o menu inicial o mundo ainda não foi criado.
        // Consultas antecipadas devem apenas informar que não existe tile.
        return gridData.GetTile(x, y);
    }

    public Tile GetTile(Vector2Int gridPos)
    {
        return GetTile(gridPos.x, gridPos.y);
    }

    /// <summary>
    /// Ponto único de escrita do líquido durante o gameplay. A simulação
    /// continua separada; este método apenas publica mudanças reais.
    /// </summary>
    public bool SetLiquidAmount(int x, int y, float amount)
    {
        Tile tile = GetTile(x, y);
        if (tile == null)
        {
            return false;
        }

        float normalizedAmount = Mathf.Max(0f, amount);
        if (tile.liquidAmount == normalizedAmount)
        {
            return false;
        }

        tile.liquidAmount = normalizedAmount;
        OnLiquidChanged?.Invoke(x, y);
        return true;
    }

    public TileType GetTileType(int x, int y)
    {
        return gridData.GetTileType(x, y, GridLayer.Terrain);
    }

    public TileType GetTileType(int x, int y, GridLayer layer)
    {
        return gridData.GetTileType(x, y, layer);
    }

    public string GetContentId(int x, int y, GridLayer layer)
    {
        return gridData.GetContentId(x, y, layer);
    }

    public void SetBuildContent(
        int x,
        int y,
        string definitionId,
        TileType tileType,
        GridLayer layer)
    {
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(definitionId);

        if (layer == GridLayer.Terrain || layer == GridLayer.Structure)
        {
            suppressLegacyStructureSpawn = true;
            try
            {
                SetTileType(x, y, tileType);
            }
            finally
            {
                suppressLegacyStructureSpawn = false;
            }
            gridData.SetContentId(x, y, layer, definitionId);
            OnLayerTileChanged?.Invoke(x, y, layer, tileType);

            if (definition != null && definition.HasPhysicalPrefab)
            {
                StructureManager.Instance?.SpawnStructure(
                    new Vector2Int(x, y),
                    definition);
            }
            return;
        }

        if (!gridData.SetTileType(x, y, layer, tileType)) return;
        gridData.SetContentId(x, y, layer, definitionId);
        OnLayerTileChanged?.Invoke(x, y, layer, tileType);
    }

    public void SetTileType(int x, int y, TileType newType, GridLayer layer)
    {
        if (newType == TileType.Empty)
        {
            gridData.SetContentId(x, y, layer, string.Empty);
        }

        if (layer == GridLayer.Terrain || layer == GridLayer.Structure)
        {
            SetTileType(x, y, newType);
            OnLayerTileChanged?.Invoke(x, y, layer, newType);
            return;
        }

        if (!gridData.SetTileType(x, y, layer, newType)) return;
        OnLayerTileChanged?.Invoke(x, y, layer, newType);
    }

    public void SetTileType(int x, int y, TileType newType)
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return;
        }

        bool createsStructure = newType == TileType.Chest
            || newType == TileType.PrintingPod;

        if (tile.type == newType && !createsStructure)
        {
            return;
        }

        if (createsStructure
            && tile.type != newType
            && !CanPlaceFootprint(
                new Vector2Int(x, y),
                StructureFootprintSettings.Resolve(newType),
                out string placementFailure))
        {
            Debug.LogWarning(
                $"[GridManager] Estrutura recusada em ({x}, {y}): {placementFailure}");
            return;
        }

        bool isSolid = IsSolidTileType(newType);

        // Evita que entidades permaneçam dentro de um bloco sólido.
        if (isSolid)
        {
            GridSafetyUtility.EjectEntitiesFromTile(x, y);
        }

        gridData.SetTileType(x, y, GridLayer.Terrain, newType);
        if (newType == TileType.Empty)
        {
            gridData.SetContentId(x, y, GridLayer.Terrain, string.Empty);
            gridData.SetContentId(x, y, GridLayer.Structure, string.Empty);
        }
        tile.isPassable = !isSolid;

        if (createsStructure && !suppressLegacyStructureSpawn)
        {
            StructureManager.Instance?.SpawnStructure(
                new Vector2Int(x, y),
                newType
            );
        }

        OnTileChanged?.Invoke(x, y, newType);
    }

    public Vector2Int WorldToGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / cellSize);
        int y = Mathf.FloorToInt(worldPosition.y / cellSize);

        return new Vector2Int(x, y);
    }

    public Vector3 GridToWorldPosition(Vector2Int gridPosition)
    {
        return new Vector3(
            gridPosition.x * cellSize + cellSize * 0.5f,
            gridPosition.y * cellSize + cellSize * 0.5f,
            0f
        );
    }

    public StorageStructure GetChestAt(Vector2Int gridPos)
    {
        return StructureManager.Instance?.GetStorageAt(gridPos);
    }

    /// <summary>
    /// Retorna true quando o tile existe e pode ser ocupado.
    /// </summary>
    public bool IsPassable(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null
            && tile.isPassable
            && !IsMovementBlocked(new Vector2Int(x, y));
    }

    public bool IsPassable(Vector2Int position)
    {
        return IsPassable(position.x, position.y);
    }

    /// <summary>
    /// Verifica apenas o terreno. Não considera footprints de estruturas.
    /// Use no rescue para não confundir occupancy com soterramento.
    /// </summary>
    public bool IsTerrainPassable(int x, int y)
    {
        Tile tile = GetTile(x, y);
        return tile != null && tile.isPassable;
    }

    public bool IsTerrainOccupiable(Vector2Int position)
    {
        return IsTerrainPassable(position.x, position.y)
            && IsTerrainPassable(position.x, position.y + 1);
    }

    /// <summary>
    /// Retorna true quando o tile é sólido.
    /// Fora do mapa também é considerado sólido.
    /// </summary>
    public bool IsSolid(int x, int y)
    {
        // As bordas externas são barreiras físicas. Isso evita que gravidade,
        // suporte e navegação interpretem o lado de fora como espaço livre.
        if (!IsInsideGrid(x, y))
        {
            return true;
        }

        Tile tile = GetTile(x, y);

        if (tile != null && !tile.isPassable)
        {
            return true;
        }

        return Occupancy.SupportsWeightAt(new Vector2Int(x, y));
    }

    public bool IsSolid(Vector2Int position)
    {
        return IsSolid(position.x, position.y);
    }

    public bool IsLadder(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && tile.type == TileType.Ladder;
    }

    public bool IsLadder(Vector2Int position)
    {
        return IsLadder(position.x, position.y);
    }

    public bool IsEmptySpace(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null && tile.type == TileType.Empty;
    }

    public bool IsEmptySpace(Vector2Int position)
    {
        return IsEmptySpace(position.x, position.y);
    }

    /// <summary>
    /// Verifica se existe suporte que a navegação pode usar abaixo do tile.
    /// Uma ocupação física tem precedência sobre o tile visual: bloquear o
    /// movimento não a torna automaticamente escalável.
    /// </summary>
    public bool HasSupportBelow(int x, int y)
    {
        return HasNavigationSupportAt(x, y - 1);
    }

    public bool HasSupportBelow(Vector2Int position)
    {
        return HasSupportBelow(position.x, position.y);
    }

    public bool HasNavigationSupportAt(int x, int y)
    {
        if (!IsInsideGrid(x, y))
        {
            return true;
        }

        Vector2Int supportCell = new Vector2Int(x, y);

        // A ocupação é a fonte de verdade para estruturas físicas. Portanto,
        // uma Chest/Pod/máquina com supportsWeight=false não vira chão apenas
        // por bloquear a célula.
        if (Occupancy.TryGetRecord(
                supportCell,
                GridLayer.Structure,
                out GridOccupancyRecord occupancy))
        {
            return !occupancy.IsBlueprint
                && occupancy.Footprint != null
                && occupancy.Footprint.supportsWeight;
        }

        Tile tile = GetTile(x, y);
        return (tile != null && !tile.isPassable)
            || IsLadder(x, y);
    }

    public bool HasNavigationSupportAt(Vector2Int position)
    {
        return HasNavigationSupportAt(position.x, position.y);
    }

    /// <summary>
    /// Verifica se uma entidade de dois tiles de altura
    /// pode ocupar esta posição.
    /// </summary>
    public bool IsStandable(int x, int y)
    {
        if (!IsPassable(x, y))
        {
            return false;
        }

        // O segundo tile representa a parte superior do corpo.
        if (!IsPassable(x, y + 1))
        {
            return false;
        }

        return HasSupportBelow(x, y);
    }

    public bool IsStandable(Vector2Int position)
    {
        return IsStandable(position.x, position.y);
    }

    /// <summary>
    /// Verifica apenas se o espaço é válido para o corpo do colono.
    /// Não exige suporte.
    /// </summary>
    public bool IsOccupiable(int x, int y)
    {
        return IsPassable(x, y)
            && IsPassable(x, y + 1);
    }

    public bool IsOccupiable(Vector2Int position)
    {
        return IsOccupiable(position.x, position.y);
    }

    public bool IsInsideGrid(int x, int y)
    {
        if (gridData.IsCreated)
        {
            return gridData.IsInside(x, y);
        }

        // Antes da criação do mundo, mantém a consulta baseada no Inspector.
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    private bool IsSolidTileType(TileType type)
    {
        return type != TileType.Empty
            && type != TileType.Ladder
            && type != TileType.Chest
            && type != TileType.PrintingPod
            && type != TileType.Structure;
    }

    public bool CanPlaceFootprint(
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        out string failureReason,
        UnityEngine.Object ignoredOwner = null)
    {
        if (footprint == null)
        {
            failureReason = "A definição da estrutura não foi encontrada.";
            return false;
        }

        Vector2Int minimum = footprint.GetMinimumCell(anchor);

        for (int localX = 0; localX < footprint.width; localX++)
        {
            for (int localY = 0; localY < footprint.height; localY++)
            {
                Vector2Int cell = minimum + new Vector2Int(localX, localY);
                Tile tile = GetTile(cell);

                if (tile == null)
                {
                    failureReason = "Parte da estrutura ficaria fora do mapa.";
                    return false;
                }

                if (tile.type != TileType.Empty)
                {
                    failureReason = "A área da estrutura contém terreno ou outra construção.";
                    return false;
                }

                if (Occupancy.IsOccupied(
                        cell,
                        GridLayer.Structure,
                        ignoredOwner))
                {
                    failureReason = "A área já está reservada por outra estrutura ou blueprint.";
                    return false;
                }
            }
        }

        if (!HasRequiredFootprintSupport(anchor, footprint))
        {
            failureReason = footprint.supportRule ==
                StructureSupportRule.EveryBottomCell
                    ? "Toda a base da estrutura precisa de apoio."
                    : "A estrutura precisa de pelo menos um ponto de apoio.";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    public bool CanPlaceFootprint(Vector2Int anchor, StructureFootprintDefinition footprint,
        GridLayer layer, out string failureReason, UnityEngine.Object ignoredOwner = null)
    {
        if (layer == GridLayer.Terrain || layer == GridLayer.Structure)
            return CanPlaceFootprint(anchor, footprint, out failureReason, ignoredOwner);
        if (footprint == null) { failureReason = "Definição de footprint ausente."; return false; }

        Vector2Int minimum = footprint.GetMinimumCell(anchor);
        for (int x = 0; x < footprint.width; x++)
        for (int y = 0; y < footprint.height; y++)
        {
            Vector2Int cell = minimum + new Vector2Int(x, y);
            if (!IsInsideGrid(cell.x, cell.y)) { failureReason = "Parte da construção ficaria fora do mapa."; return false; }
            if (GetTileType(cell.x, cell.y, layer) != TileType.Empty) { failureReason = "Essa camada já está ocupada."; return false; }
            if (Occupancy.IsOccupied(cell, layer, ignoredOwner))
            { failureReason = "Essa camada já possui um blueprint."; return false; }
        }
        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Valida um plano que pode exigir escavação antes da construção.
    /// Não libera Bedrock, estruturas existentes ou footprints ocupados.
    /// </summary>
    public bool CanPlanFootprint(
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        GridLayer layer,
        out string failureReason,
        UnityEngine.Object ignoredOwner = null)
    {
        if (layer != GridLayer.Terrain && layer != GridLayer.Structure)
        {
            return CanPlaceFootprint(
                anchor,
                footprint,
                layer,
                out failureReason,
                ignoredOwner);
        }

        if (footprint == null)
        {
            failureReason = "Definição de footprint ausente.";
            return false;
        }

        Vector2Int minimum = footprint.GetMinimumCell(anchor);
        for (int localX = 0; localX < footprint.width; localX++)
        {
            for (int localY = 0; localY < footprint.height; localY++)
            {
                Vector2Int cell = minimum + new Vector2Int(localX, localY);
                Tile tile = GetTile(cell);

                if (tile == null)
                {
                    failureReason = "Parte da construção ficaria fora do mapa.";
                    return false;
                }

                if (tile.type == TileType.Bedrock)
                {
                    failureReason = "Bedrock não pode ser removido para uma construção.";
                    return false;
                }

                if (tile.type != TileType.Empty
                    && !IsTerrainRemovableForBuildPlan(tile.type))
                {
                    failureReason = "A célula contém uma construção que não pode ser substituída automaticamente.";
                    return false;
                }

                if (Occupancy.IsOccupied(
                        cell,
                        GridLayer.Structure,
                        ignoredOwner))
                {
                    failureReason = "A área já está reservada por outra estrutura ou blueprint.";
                    return false;
                }
            }
        }

        if (!HasRequiredFootprintSupport(anchor, footprint))
        {
            failureReason = footprint.supportRule ==
                StructureSupportRule.EveryBottomCell
                    ? "Toda a base planejada precisa de apoio."
                    : "A construção planejada precisa de apoio.";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    public bool IsTerrainRemovableForBuildPlan(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Solid:
            case TileType.Grass:
            case TileType.Stone:
            case TileType.Copper:
            case TileType.Coal:
            case TileType.Iron:
            case TileType.Gold:
                return true;

            default:
                return false;
        }
    }

    public bool RegisterFootprint(
        UnityEngine.Object owner,
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        bool isBlueprint)
    {
        return Occupancy.Register(
            owner,
            anchor,
            footprint,
            GridLayer.Structure,
            isBlueprint
                ? GridOccupancyKind.Blueprint
                : GridOccupancyKind.Structure);
    }

    public bool RegisterFootprint(UnityEngine.Object owner, Vector2Int anchor,
        StructureFootprintDefinition footprint, bool isBlueprint, GridLayer layer)
    {
        return Occupancy.Register(
            owner,
            anchor,
            footprint,
            layer,
            isBlueprint
                ? GridOccupancyKind.Blueprint
                : GridOccupancyKind.Structure);
    }

    public void UnregisterFootprint(UnityEngine.Object owner)
    {
        Occupancy.Unregister(owner);
    }

    public void UnregisterFootprint(
        UnityEngine.Object owner,
        GridLayer layer)
    {
        Occupancy.Unregister(owner, layer);
    }

    public UnityEngine.Object GetOccupantAt(Vector2Int position)
    {
        return Occupancy.GetOccupantAt(position);
    }

    public UnityEngine.Object GetOccupantAt(
        Vector2Int position,
        GridLayer layer)
    {
        return Occupancy.GetOccupantAt(position, layer);
    }

    public bool TryGetOccupancy(
        Vector2Int position,
        GridLayer layer,
        out GridOccupancyRecord record)
    {
        return Occupancy.TryGetRecord(position, layer, out record);
    }

    public bool IsFootprintOccupied(Vector2Int position)
    {
        return Occupancy.IsOccupied(position, GridLayer.Structure);
    }

    public bool IsFootprintOccupied(
        Vector2Int position,
        GridLayer layer)
    {
        return Occupancy.IsOccupied(position, layer);
    }

    public bool TryGetInteractionCells(
        Vector2Int targetPosition,
        List<Vector2Int> results)
    {
        return Occupancy.TryGetInteractionCells(
            targetPosition,
            results,
            IsStandable);
    }

    private bool IsMovementBlocked(Vector2Int position)
    {
        return Occupancy.BlocksMovementAt(position);
    }

    private bool HasRequiredFootprintSupport(
        Vector2Int anchor,
        StructureFootprintDefinition footprint)
    {
        if (footprint.supportRule == StructureSupportRule.None)
        {
            return true;
        }

        Vector2Int minimum = footprint.GetMinimumCell(anchor);
        int supportedCells = 0;

        for (int localX = 0; localX < footprint.width; localX++)
        {
            Vector2Int supportCell = new Vector2Int(
                minimum.x + localX,
                minimum.y - 1);

            if (IsSolid(supportCell) || IsLadder(supportCell))
            {
                supportedCells++;
            }
        }

        return footprint.supportRule == StructureSupportRule.EveryBottomCell
            ? supportedCells == footprint.width
            : supportedCells > 0;
    }

    public bool IsRequiredStructureSupport(
        Vector2Int terrainCell,
        out GridOccupancyRecord supportedRecord)
    {
        supportedRecord = null;

        foreach (GridOccupancyRecord record in
            Occupancy.GetRecordsSnapshot(GridLayer.Structure))
        {
            if (record == null || record.Footprint == null
                || record.Footprint.supportRule == StructureSupportRule.None)
            {
                continue;
            }

            Vector2Int minimum = record.Footprint.GetMinimumCell(record.Anchor);
            if (terrainCell.y != minimum.y - 1
                || terrainCell.x < minimum.x
                || terrainCell.x >= minimum.x + record.Footprint.width)
            {
                continue;
            }

            if (record.Footprint.supportRule == StructureSupportRule.EveryBottomCell)
            {
                supportedRecord = record;
                return true;
            }

            bool hasOtherSupport = false;
            for (int localX = 0; localX < record.Footprint.width; localX++)
            {
                Vector2Int other = new Vector2Int(
                    minimum.x + localX,
                    minimum.y - 1);
                if (other != terrainCell && (IsSolid(other) || IsLadder(other)))
                {
                    hasOtherSupport = true;
                    break;
                }
            }

            if (!hasOtherSupport)
            {
                supportedRecord = record;
                return true;
            }
        }

        return false;
    }

    private void ClearOccupancy()
    {
        Occupancy.Clear();
    }

    private void HandleOccupancyChanged(
        GridOccupancyRecord record,
        GridOccupancyChangeType changeType)
    {
        OnOccupancyChanged?.Invoke(record, changeType);

        if (record.Layer != GridLayer.Terrain
            && record.Layer != GridLayer.Structure)
        {
            return;
        }

        // Compatibilidade: os sistemas atuais de navegação ainda escutam
        // OnTileChanged quando a ocupação física muda.
        foreach (Vector2Int cell in record.Cells)
        {
            OnTileChanged?.Invoke(
                cell.x,
                cell.y,
                GetTileType(cell.x, cell.y));
        }
    }
}
