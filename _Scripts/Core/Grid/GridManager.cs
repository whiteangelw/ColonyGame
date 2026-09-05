using System;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : Singleton<GridManager>, IGridService
{
    [Header("Configurações do Mapa")]
    public int width = 20;
    public int height = 15;
    public float cellSize = 1.0f;

    public bool IsGridReady { get; private set; }

    public int Width => width;
    public int Height => height;

    public event Action<int, int, TileType> OnTileChanged;
    public event Action<int, int, GridLayer, TileType> OnLayerTileChanged;
    public event Action OnGridRebuilt;

    private Tile[,] grid;

    private sealed class GridOccupant
    {
        public UnityEngine.Object owner;
        public Vector2Int anchor;
        public StructureFootprintDefinition footprint;
        public bool isBlueprint;
    }

    private readonly Dictionary<Vector2Int, GridOccupant> occupantsByCell =
        new Dictionary<Vector2Int, GridOccupant>();

    private readonly Dictionary<UnityEngine.Object, GridOccupant> occupantsByOwner =
        new Dictionary<UnityEngine.Object, GridOccupant>();

    private readonly Dictionary<Vector2Int, UnityEngine.Object> backWallReservations =
        new Dictionary<Vector2Int, UnityEngine.Object>();
    private readonly Dictionary<Vector2Int, UnityEngine.Object> decorationReservations =
        new Dictionary<Vector2Int, UnityEngine.Object>();
    private readonly Dictionary<UnityEngine.Object, List<Vector2Int>> layeredReservations =
        new Dictionary<UnityEngine.Object, List<Vector2Int>>();

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

        grid = new Tile[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Tile(x, y, TileType.Empty);
            }
        }

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
        grid = new Tile[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Tile(x, y, TileType.Empty);
            }
        }
    }

    public void RestoreTileState(
        int x,
        int y,
        TileType type,
        FogState fogState,
        float liquidAmount,
        TileType backWallType = TileType.Empty,
        TileType decorationType = TileType.Empty)
    {
        Tile tile = GetTile(x, y);

        if (tile == null)
        {
            return;
        }

        tile.type = type;
        tile.backWallType = backWallType;
        tile.decorationType = decorationType;
        tile.isPassable = !IsSolidTileType(type);
        tile.fogState = fogState;
        tile.liquidAmount = Mathf.Max(0f, liquidAmount);
        tile.reachabilityGroupID = -1;
    }

    public void CompleteSnapshotRestore()
    {
        IsGridReady = true;
        OnGridRebuilt?.Invoke();
    }

    public Tile GetTile(int x, int y)
    {
        // Durante o menu inicial o mundo ainda não foi criado.
        // Consultas antecipadas devem apenas informar que não existe tile.
        if (grid == null || !IsInsideGrid(x, y))
        {
            return null;
        }

        return grid[x, y];
    }

    public Tile GetTile(Vector2Int gridPos)
    {
        return GetTile(gridPos.x, gridPos.y);
    }

    public TileType GetTileType(int x, int y)
    {
        Tile tile = GetTile(x, y);

        return tile != null
            ? tile.type
            : TileType.Empty;
    }

    public TileType GetTileType(int x, int y, GridLayer layer)
    {
        Tile tile = GetTile(x, y);
        if (tile == null) return TileType.Empty;
        return layer switch
        {
            GridLayer.BackWall => tile.backWallType,
            GridLayer.Decoration => tile.decorationType,
            _ => tile.type
        };
    }

    public void SetTileType(int x, int y, TileType newType, GridLayer layer)
    {
        if (layer == GridLayer.Terrain || layer == GridLayer.Structure)
        {
            SetTileType(x, y, newType);
            OnLayerTileChanged?.Invoke(x, y, layer, newType);
            return;
        }

        Tile tile = GetTile(x, y);
        if (tile == null) return;
        if (layer == GridLayer.BackWall) tile.backWallType = newType;
        else tile.decorationType = newType;
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

        tile.type = newType;
        tile.isPassable = !isSolid;

        if (createsStructure)
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
    /// Retorna true quando o tile é sólido.
    /// Fora do mapa também é considerado sólido.
    /// </summary>
    public bool IsSolid(int x, int y)
    {
        Tile tile = GetTile(x, y);

        if (tile != null && !tile.isPassable)
        {
            return true;
        }

        return occupantsByCell.TryGetValue(
                new Vector2Int(x, y),
                out GridOccupant occupant)
            && !occupant.isBlueprint
            && occupant.footprint.supportsWeight;
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
    /// Verifica se existe alguma forma de suporte físico abaixo do tile.
    /// </summary>
    public bool HasSupportBelow(int x, int y)
    {
        return IsSolid(x, y - 1) || IsLadder(x, y - 1);
    }

    public bool HasSupportBelow(Vector2Int position)
    {
        return HasSupportBelow(position.x, position.y);
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
        return x >= 0
            && x < width
            && y >= 0
            && y < height;
    }

    private bool IsSolidTileType(TileType type)
    {
        return type != TileType.Empty
            && type != TileType.Ladder
            && type != TileType.Chest
            && type != TileType.PrintingPod;
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

                if (occupantsByCell.TryGetValue(cell, out GridOccupant occupant)
                    && occupant.owner != ignoredOwner)
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

        Dictionary<Vector2Int, UnityEngine.Object> reservations =
            layer == GridLayer.BackWall ? backWallReservations : decorationReservations;
        Vector2Int minimum = footprint.GetMinimumCell(anchor);
        for (int x = 0; x < footprint.width; x++)
        for (int y = 0; y < footprint.height; y++)
        {
            Vector2Int cell = minimum + new Vector2Int(x, y);
            if (!IsInsideGrid(cell.x, cell.y)) { failureReason = "Parte da construção ficaria fora do mapa."; return false; }
            if (GetTileType(cell.x, cell.y, layer) != TileType.Empty) { failureReason = "Essa camada já está ocupada."; return false; }
            if (reservations.TryGetValue(cell, out UnityEngine.Object owner) && owner != ignoredOwner)
            { failureReason = "Essa camada já possui um blueprint."; return false; }
        }
        failureReason = string.Empty;
        return true;
    }

    public bool RegisterFootprint(
        UnityEngine.Object owner,
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        bool isBlueprint)
    {
        if (owner == null || footprint == null)
        {
            return false;
        }

        UnregisterFootprint(owner);
        Vector2Int minimum = footprint.GetMinimumCell(anchor);

        for (int localX = 0; localX < footprint.width; localX++)
        {
            for (int localY = 0; localY < footprint.height; localY++)
            {
                Vector2Int cell = minimum + new Vector2Int(localX, localY);

                if (!IsInsideGrid(cell.x, cell.y)
                    || occupantsByCell.ContainsKey(cell))
                {
                    return false;
                }
            }
        }

        GridOccupant record = new GridOccupant
        {
            owner = owner,
            anchor = anchor,
            footprint = footprint,
            isBlueprint = isBlueprint
        };

        occupantsByOwner[owner] = record;

        for (int localX = 0; localX < footprint.width; localX++)
        {
            for (int localY = 0; localY < footprint.height; localY++)
            {
                Vector2Int cell = minimum + new Vector2Int(localX, localY);
                occupantsByCell[cell] = record;
                OnTileChanged?.Invoke(cell.x, cell.y, GetTileType(cell.x, cell.y));
            }
        }

        return true;
    }

    public bool RegisterFootprint(UnityEngine.Object owner, Vector2Int anchor,
        StructureFootprintDefinition footprint, bool isBlueprint, GridLayer layer)
    {
        if (layer == GridLayer.Terrain || layer == GridLayer.Structure)
            return RegisterFootprint(owner, anchor, footprint, isBlueprint);
        if (!CanPlaceFootprint(anchor, footprint, layer, out _, owner)) return false;
        UnregisterFootprint(owner);
        Dictionary<Vector2Int, UnityEngine.Object> reservations =
            layer == GridLayer.BackWall ? backWallReservations : decorationReservations;
        List<Vector2Int> cells = new List<Vector2Int>();
        Vector2Int minimum = footprint.GetMinimumCell(anchor);
        for (int x = 0; x < footprint.width; x++)
        for (int y = 0; y < footprint.height; y++)
        {
            Vector2Int cell = minimum + new Vector2Int(x, y);
            reservations[cell] = owner;
            cells.Add(cell);
        }
        layeredReservations[owner] = cells;
        return true;
    }

    public void UnregisterFootprint(UnityEngine.Object owner)
    {
        if (owner != null && layeredReservations.TryGetValue(owner, out List<Vector2Int> layeredCells))
        {
            foreach (Vector2Int cell in layeredCells)
            {
                backWallReservations.Remove(cell);
                decorationReservations.Remove(cell);
            }
            layeredReservations.Remove(owner);
        }
        if (owner == null
            || !occupantsByOwner.TryGetValue(owner, out GridOccupant record))
        {
            return;
        }

        List<Vector2Int> releasedCells = new List<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, GridOccupant> entry in occupantsByCell)
        {
            if (entry.Value == record)
            {
                releasedCells.Add(entry.Key);
            }
        }

        foreach (Vector2Int cell in releasedCells)
        {
            occupantsByCell.Remove(cell);
            OnTileChanged?.Invoke(cell.x, cell.y, GetTileType(cell.x, cell.y));
        }

        occupantsByOwner.Remove(owner);
    }

    public UnityEngine.Object GetOccupantAt(Vector2Int position)
    {
        return occupantsByCell.TryGetValue(position, out GridOccupant occupant)
            ? occupant.owner
            : null;
    }

    public bool IsFootprintOccupied(Vector2Int position)
    {
        return occupantsByCell.ContainsKey(position);
    }

    public bool TryGetInteractionCells(
        Vector2Int targetPosition,
        List<Vector2Int> results)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();

        if (!occupantsByCell.TryGetValue(targetPosition, out GridOccupant record))
        {
            foreach (GridOccupant candidate in occupantsByOwner.Values)
            {
                if (candidate.anchor == targetPosition)
                {
                    record = candidate;
                    break;
                }
            }
        }

        if (record == null)
        {
            return false;
        }

        Vector2Int minimum = record.footprint.GetMinimumCell(record.anchor);
        int minX = minimum.x;
        int maxX = minimum.x + record.footprint.width - 1;
        int minY = minimum.y;
        int maxY = minimum.y + record.footprint.height - 1;

        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            AddInteractionCell(results, new Vector2Int(x, minY - 1));
            AddInteractionCell(results, new Vector2Int(x, maxY + 1));
        }

        for (int y = minY; y <= maxY; y++)
        {
            AddInteractionCell(results, new Vector2Int(minX - 1, y));
            AddInteractionCell(results, new Vector2Int(maxX + 1, y));
        }

        return results.Count > 0;
    }

    private bool IsMovementBlocked(Vector2Int position)
    {
        return occupantsByCell.TryGetValue(position, out GridOccupant occupant)
            && !occupant.isBlueprint
            && occupant.footprint.blocksMovement;
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

    private void AddInteractionCell(
        List<Vector2Int> results,
        Vector2Int position)
    {
        if (IsStandable(position) && !results.Contains(position))
        {
            results.Add(position);
        }
    }

    private void ClearOccupancy()
    {
        occupantsByCell.Clear();
        occupantsByOwner.Clear();
        backWallReservations.Clear();
        decorationReservations.Clear();
        layeredReservations.Clear();
    }
}
