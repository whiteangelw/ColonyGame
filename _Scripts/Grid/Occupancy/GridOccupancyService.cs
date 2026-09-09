using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Armazena footprints e reservas por camada. Não consulta terreno,
/// não valida suporte físico e não executa regras de construção.
/// </summary>
public sealed class GridOccupancyService
{
    private readonly Func<int, int, bool> isInsideGrid;

    private readonly Dictionary<GridLayer, Dictionary<Vector2Int, GridOccupancyRecord>>
        recordsByLayer =
            new Dictionary<GridLayer, Dictionary<Vector2Int, GridOccupancyRecord>>();

    private readonly Dictionary<UnityEngine.Object, List<GridOccupancyRecord>>
        recordsByOwner =
            new Dictionary<UnityEngine.Object, List<GridOccupancyRecord>>();

    public event Action<GridOccupancyRecord, GridOccupancyChangeType>
        OccupancyChanged;

    public GridOccupancyService(Func<int, int, bool> isInsideGrid)
    {
        this.isInsideGrid = isInsideGrid
            ?? throw new ArgumentNullException(nameof(isInsideGrid));

        recordsByLayer[GridLayer.Structure] =
            new Dictionary<Vector2Int, GridOccupancyRecord>();
        recordsByLayer[GridLayer.BackWall] =
            new Dictionary<Vector2Int, GridOccupancyRecord>();
        recordsByLayer[GridLayer.Decoration] =
            new Dictionary<Vector2Int, GridOccupancyRecord>();
    }

    public bool Register(
        UnityEngine.Object owner,
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        GridLayer layer,
        GridOccupancyKind kind)
    {
        if (owner == null || footprint == null)
        {
            return false;
        }

        GridLayer storageLayer = NormalizeLayer(layer);
        Dictionary<Vector2Int, GridOccupancyRecord> layerRecords =
            recordsByLayer[storageLayer];
        List<Vector2Int> requestedCells = BuildCells(anchor, footprint);

        // Valida tudo antes de remover um registro anterior do mesmo owner.
        foreach (Vector2Int cell in requestedCells)
        {
            if (!isInsideGrid(cell.x, cell.y))
            {
                return false;
            }

            if (layerRecords.TryGetValue(cell, out GridOccupancyRecord existing)
                && existing.Owner != owner)
            {
                return false;
            }
        }

        Unregister(owner, layer);

        GridOccupancyRecord record = new GridOccupancyRecord(
            owner,
            anchor,
            footprint,
            layer,
            kind,
            requestedCells);

        foreach (Vector2Int cell in requestedCells)
        {
            layerRecords[cell] = record;
        }

        if (!recordsByOwner.TryGetValue(
                owner,
                out List<GridOccupancyRecord> ownerRecords))
        {
            ownerRecords = new List<GridOccupancyRecord>();
            recordsByOwner.Add(owner, ownerRecords);
        }

        ownerRecords.Add(record);
        OccupancyChanged?.Invoke(
            record,
            GridOccupancyChangeType.Registered);
        return true;
    }

    public void Unregister(UnityEngine.Object owner)
    {
        if (owner == null
            || !recordsByOwner.TryGetValue(
                owner,
                out List<GridOccupancyRecord> records))
        {
            return;
        }

        GridOccupancyRecord[] snapshot = records.ToArray();
        foreach (GridOccupancyRecord record in snapshot)
        {
            RemoveRecord(record);
        }
    }

    public void Unregister(UnityEngine.Object owner, GridLayer layer)
    {
        GridOccupancyRecord record = GetRecordOwnedBy(owner, layer);
        if (record != null)
        {
            RemoveRecord(record);
        }
    }

    public UnityEngine.Object GetOccupantAt(Vector2Int position)
    {
        return GetOccupantAt(position, GridLayer.Structure);
    }

    public UnityEngine.Object GetOccupantAt(
        Vector2Int position,
        GridLayer layer)
    {
        return TryGetRecord(position, layer, out GridOccupancyRecord record)
            ? record.Owner
            : null;
    }

    public bool TryGetRecord(
        Vector2Int position,
        GridLayer layer,
        out GridOccupancyRecord record)
    {
        return recordsByLayer[NormalizeLayer(layer)].TryGetValue(
            position,
            out record);
    }

    public bool IsOccupied(
        Vector2Int position,
        GridLayer layer,
        UnityEngine.Object ignoredOwner = null)
    {
        if (!TryGetRecord(position, layer, out GridOccupancyRecord record))
        {
            return false;
        }

        return ignoredOwner == null || record.Owner != ignoredOwner;
    }

    public bool BlocksMovementAt(Vector2Int position)
    {
        return TryGetRecord(
                position,
                GridLayer.Structure,
                out GridOccupancyRecord record)
            && !record.IsBlueprint
            && record.Footprint.blocksMovement;
    }

    public bool SupportsWeightAt(Vector2Int position)
    {
        return TryGetRecord(
                position,
                GridLayer.Structure,
                out GridOccupancyRecord record)
            && !record.IsBlueprint
            && record.Footprint.supportsWeight;
    }

    public List<GridOccupancyRecord> GetRecordsSnapshot(GridLayer layer)
    {
        GridLayer normalizedLayer = NormalizeLayer(layer);
        List<GridOccupancyRecord> result = new List<GridOccupancyRecord>();

        foreach (List<GridOccupancyRecord> ownerRecords in recordsByOwner.Values)
        {
            foreach (GridOccupancyRecord record in ownerRecords)
            {
                if (record != null
                    && NormalizeLayer(record.Layer) == normalizedLayer
                    && !result.Contains(record))
                {
                    result.Add(record);
                }
            }
        }

        return result;
    }

    public bool TryGetInteractionCells(
        Vector2Int targetPosition,
        List<Vector2Int> results,
        Func<Vector2Int, bool> isStandable)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();

        if (!TryGetRecord(
                targetPosition,
                GridLayer.Structure,
                out GridOccupancyRecord record))
        {
            record = FindPhysicalRecordByAnchor(targetPosition);
        }

        if (record == null)
        {
            return false;
        }

        Vector2Int minimum = record.Footprint.GetMinimumCell(record.Anchor);
        int maximumX = minimum.x + record.Footprint.width - 1;
        int maximumY = minimum.y + record.Footprint.height - 1;

        for (int x = minimum.x - 1; x <= maximumX + 1; x++)
        {
            AddInteractionCell(
                results,
                new Vector2Int(x, minimum.y - 1),
                isStandable);
            AddInteractionCell(
                results,
                new Vector2Int(x, maximumY + 1),
                isStandable);
        }

        for (int y = minimum.y; y <= maximumY; y++)
        {
            AddInteractionCell(
                results,
                new Vector2Int(minimum.x - 1, y),
                isStandable);
            AddInteractionCell(
                results,
                new Vector2Int(maximumX + 1, y),
                isStandable);
        }

        return results.Count > 0;
    }

    public void Clear()
    {
        foreach (Dictionary<Vector2Int, GridOccupancyRecord> records
            in recordsByLayer.Values)
        {
            records.Clear();
        }

        recordsByOwner.Clear();
    }

    private static GridLayer NormalizeLayer(GridLayer layer)
    {
        // Terrain e Structure continuam concorrendo pelo mesmo espaço físico.
        switch (layer)
        {
            case GridLayer.BackWall:
                return GridLayer.BackWall;

            case GridLayer.Decoration:
                return GridLayer.Decoration;

            default:
                return GridLayer.Structure;
        }
    }

    private static List<Vector2Int> BuildCells(
        Vector2Int anchor,
        StructureFootprintDefinition footprint)
    {
        List<Vector2Int> cells = new List<Vector2Int>(
            footprint.width * footprint.height);
        Vector2Int minimum = footprint.GetMinimumCell(anchor);

        for (int localX = 0; localX < footprint.width; localX++)
        {
            for (int localY = 0; localY < footprint.height; localY++)
            {
                cells.Add(minimum + new Vector2Int(localX, localY));
            }
        }

        return cells;
    }

    private GridOccupancyRecord GetRecordOwnedBy(
        UnityEngine.Object owner,
        GridLayer layer)
    {
        if (owner == null
            || !recordsByOwner.TryGetValue(
                owner,
                out List<GridOccupancyRecord> records))
        {
            return null;
        }

        GridLayer normalizedLayer = NormalizeLayer(layer);
        return records.Find(record =>
            NormalizeLayer(record.Layer) == normalizedLayer);
    }

    private GridOccupancyRecord FindPhysicalRecordByAnchor(
        Vector2Int anchor)
    {
        foreach (List<GridOccupancyRecord> records in recordsByOwner.Values)
        {
            foreach (GridOccupancyRecord record in records)
            {
                if (NormalizeLayer(record.Layer) == GridLayer.Structure
                    && record.Anchor == anchor)
                {
                    return record;
                }
            }
        }

        return null;
    }

    private void RemoveRecord(GridOccupancyRecord record)
    {
        Dictionary<Vector2Int, GridOccupancyRecord> layerRecords =
            recordsByLayer[NormalizeLayer(record.Layer)];

        foreach (Vector2Int cell in record.Cells)
        {
            if (layerRecords.TryGetValue(cell, out GridOccupancyRecord current)
                && ReferenceEquals(current, record))
            {
                layerRecords.Remove(cell);
            }
        }

        if (recordsByOwner.TryGetValue(
                record.Owner,
                out List<GridOccupancyRecord> ownerRecords))
        {
            ownerRecords.Remove(record);
            if (ownerRecords.Count == 0)
            {
                recordsByOwner.Remove(record.Owner);
            }
        }

        OccupancyChanged?.Invoke(
            record,
            GridOccupancyChangeType.Unregistered);
    }

    private static void AddInteractionCell(
        List<Vector2Int> results,
        Vector2Int position,
        Func<Vector2Int, bool> isStandable)
    {
        if ((isStandable == null || isStandable(position))
            && !results.Contains(position))
        {
            results.Add(position);
        }
    }
}
