using System.Collections.Generic;
using UnityEngine;

public enum GridOccupancyKind
{
    Blueprint,
    Structure
}

public enum GridOccupancyChangeType
{
    Registered,
    Unregistered
}

/// <summary>
/// Descreve quem ocupa uma área, em qual camada e com qual footprint.
/// </summary>
public sealed class GridOccupancyRecord
{
    private readonly List<Vector2Int> cells;

    public UnityEngine.Object Owner { get; }
    public Vector2Int Anchor { get; }
    public StructureFootprintDefinition Footprint { get; }
    public GridLayer Layer { get; }
    public GridOccupancyKind Kind { get; }
    public IReadOnlyList<Vector2Int> Cells => cells;
    public bool IsBlueprint => Kind == GridOccupancyKind.Blueprint;

    public GridOccupancyRecord(
        UnityEngine.Object owner,
        Vector2Int anchor,
        StructureFootprintDefinition footprint,
        GridLayer layer,
        GridOccupancyKind kind,
        List<Vector2Int> occupiedCells)
    {
        Owner = owner;
        Anchor = anchor;
        Footprint = footprint;
        Layer = layer;
        Kind = kind;
        cells = occupiedCells;
    }
}
