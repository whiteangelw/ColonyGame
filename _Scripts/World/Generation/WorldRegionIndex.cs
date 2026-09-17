using System;
using System.Collections.Generic;

[Flags]
public enum WorldRegionDirtyFlags
{
    None = 0,
    Terrain = 1 << 0,
    Navigation = 1 << 1,
    Liquid = 1 << 2,
    Fog = 1 << 3,
    Visual = 1 << 4,
    All = Terrain | Navigation | Liquid | Fog | Visual
}

public readonly struct WorldRegionCoordinate : IEquatable<WorldRegionCoordinate>
{
    public int X { get; }
    public int Y { get; }

    public WorldRegionCoordinate(int x, int y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(WorldRegionCoordinate other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object obj)
    {
        return obj is WorldRegionCoordinate other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked { return (X * 397) ^ Y; }
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public readonly struct WorldRegionBounds
{
    public int MinimumX { get; }
    public int MinimumY { get; }
    public int MaximumX { get; }
    public int MaximumY { get; }
    public int Width => MaximumX - MinimumX + 1;
    public int Height => MaximumY - MinimumY + 1;

    public WorldRegionBounds(
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        MinimumX = minimumX;
        MinimumY = minimumY;
        MaximumX = maximumX;
        MaximumY = maximumY;
    }

    public bool Contains(int x, int y)
    {
        return x >= MinimumX && x <= MaximumX
            && y >= MinimumY && y <= MaximumY;
    }
}

public sealed class WorldRegionState
{
    public WorldRegionCoordinate Coordinate { get; }
    public WorldRegionBounds Bounds { get; }
    public WorldRegionDirtyFlags DirtyFlags { get; private set; }
    public bool IsDirty => DirtyFlags != WorldRegionDirtyFlags.None;

    internal WorldRegionState(
        WorldRegionCoordinate coordinate,
        WorldRegionBounds bounds)
    {
        Coordinate = coordinate;
        Bounds = bounds;
    }

    internal WorldRegionDirtyFlags MarkDirty(WorldRegionDirtyFlags flags)
    {
        WorldRegionDirtyFlags added = flags & ~DirtyFlags;
        DirtyFlags |= flags;
        return added;
    }

    internal void ClearDirty(WorldRegionDirtyFlags flags)
    {
        DirtyFlags &= ~flags;
    }
}

/// <summary>
/// Índice espacial leve. Não armazena tiles e não executa sistemas.
/// </summary>
public sealed class WorldRegionIndex
{
    private WorldRegionState[] regions = Array.Empty<WorldRegionState>();

    public int WorldWidth { get; private set; }
    public int WorldHeight { get; private set; }
    public int RegionSize { get; private set; }
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int Count => regions.Length;
    public bool IsInitialized => regions.Length > 0;

    public event Action<WorldRegionState, WorldRegionDirtyFlags>
        RegionMarkedDirty;

    public void Initialize(int worldWidth, int worldHeight, int regionSize)
    {
        WorldWidth = Math.Max(1, worldWidth);
        WorldHeight = Math.Max(1, worldHeight);
        RegionSize = Math.Max(1, regionSize);
        Columns = (WorldWidth + RegionSize - 1) / RegionSize;
        Rows = (WorldHeight + RegionSize - 1) / RegionSize;
        regions = new WorldRegionState[checked(Columns * Rows)];

        for (int regionY = 0; regionY < Rows; regionY++)
        {
            for (int regionX = 0; regionX < Columns; regionX++)
            {
                int minimumX = regionX * RegionSize;
                int minimumY = regionY * RegionSize;
                int maximumX = Math.Min(
                    WorldWidth - 1,
                    minimumX + RegionSize - 1);
                int maximumY = Math.Min(
                    WorldHeight - 1,
                    minimumY + RegionSize - 1);
                regions[ToIndex(regionX, regionY)] = new WorldRegionState(
                    new WorldRegionCoordinate(regionX, regionY),
                    new WorldRegionBounds(
                        minimumX,
                        minimumY,
                        maximumX,
                        maximumY));
            }
        }
    }

    public bool TryGetByCell(int cellX, int cellY, out WorldRegionState region)
    {
        region = null;
        if (!IsInsideWorld(cellX, cellY) || !IsInitialized) return false;
        region = regions[ToIndex(cellX / RegionSize, cellY / RegionSize)];
        return true;
    }

    public bool TryGet(
        WorldRegionCoordinate coordinate,
        out WorldRegionState region)
    {
        region = null;
        if (coordinate.X < 0 || coordinate.X >= Columns
            || coordinate.Y < 0 || coordinate.Y >= Rows)
        {
            return false;
        }

        region = regions[ToIndex(coordinate.X, coordinate.Y)];
        return true;
    }

    public void MarkCellDirty(
        int cellX,
        int cellY,
        WorldRegionDirtyFlags flags,
        int paddingInCells = 0)
    {
        if (flags == WorldRegionDirtyFlags.None || !IsInitialized) return;
        int padding = Math.Max(0, paddingInCells);
        MarkBoundsDirty(
            cellX - padding,
            cellY - padding,
            cellX + padding,
            cellY + padding,
            flags);
    }

    public void MarkBoundsDirty(
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        WorldRegionDirtyFlags flags)
    {
        if (flags == WorldRegionDirtyFlags.None || !IsInitialized) return;

        minimumX = Math.Max(0, minimumX);
        minimumY = Math.Max(0, minimumY);
        maximumX = Math.Min(WorldWidth - 1, maximumX);
        maximumY = Math.Min(WorldHeight - 1, maximumY);
        if (minimumX > maximumX || minimumY > maximumY) return;

        int firstRegionX = minimumX / RegionSize;
        int firstRegionY = minimumY / RegionSize;
        int lastRegionX = maximumX / RegionSize;
        int lastRegionY = maximumY / RegionSize;

        for (int regionY = firstRegionY; regionY <= lastRegionY; regionY++)
        {
            for (int regionX = firstRegionX;
                 regionX <= lastRegionX;
                 regionX++)
            {
                MarkRegion(regions[ToIndex(regionX, regionY)], flags);
            }
        }
    }

    public void MarkAllDirty(WorldRegionDirtyFlags flags)
    {
        if (flags == WorldRegionDirtyFlags.None) return;
        for (int i = 0; i < regions.Length; i++) MarkRegion(regions[i], flags);
    }

    public void ClearDirty(
        WorldRegionCoordinate coordinate,
        WorldRegionDirtyFlags flags)
    {
        if (TryGet(coordinate, out WorldRegionState region))
        {
            region.ClearDirty(flags);
        }
    }

    public void ClearAllDirty(WorldRegionDirtyFlags flags)
    {
        for (int i = 0; i < regions.Length; i++) regions[i].ClearDirty(flags);
    }

    public int CollectDirty(
        WorldRegionDirtyFlags requiredFlags,
        List<WorldRegionState> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        for (int i = 0; i < regions.Length; i++)
        {
            if ((regions[i].DirtyFlags & requiredFlags) != 0)
            {
                destination.Add(regions[i]);
            }
        }

        return destination.Count;
    }

    public int CountDirty(WorldRegionDirtyFlags requiredFlags)
    {
        int count = 0;
        for (int i = 0; i < regions.Length; i++)
        {
            if ((regions[i].DirtyFlags & requiredFlags) != 0) count++;
        }

        return count;
    }

    private void MarkRegion(
        WorldRegionState region,
        WorldRegionDirtyFlags flags)
    {
        WorldRegionDirtyFlags added = region.MarkDirty(flags);
        if (added != WorldRegionDirtyFlags.None)
        {
            RegionMarkedDirty?.Invoke(region, added);
        }
    }

    private bool IsInsideWorld(int x, int y)
    {
        return x >= 0 && x < WorldWidth && y >= 0 && y < WorldHeight;
    }

    private int ToIndex(int regionX, int regionY)
    {
        return regionX + regionY * Columns;
    }
}
