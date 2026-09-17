using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snapshot produzido pela geração antes de entrar no grid de runtime.
/// </summary>
public sealed class WorldGenerationResult
{
    private readonly WorldCellData[,] cells;
    private readonly string[] biomeIds;
    private readonly List<GeneratedFloraData> generatedFlora =
        new List<GeneratedFloraData>();
    private readonly HashSet<int> floraCells = new HashSet<int>();

    public int Width { get; }
    public int Height { get; }
    public float Seed { get; }
    public int GenerationVersion { get; }
    public Vector2Int SpawnPosition { get; internal set; }
    public IReadOnlyList<GeneratedFloraData> GeneratedFlora => generatedFlora;
    public int GeneratedFloraCount => generatedFlora.Count;

    public WorldGenerationResult(
        int width,
        int height,
        float seed,
        int generationVersion)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Seed = seed;
        GenerationVersion = Math.Max(1, generationVersion);
        cells = new WorldCellData[Width, Height];
        biomeIds = new string[Width * Height];
    }

    public WorldCellData GetCell(int x, int y)
    {
        return IsInside(x, y) ? cells[x, y] : default;
    }

    internal void SetCell(int x, int y, WorldCellData value)
    {
        if (IsInside(x, y)) cells[x, y] = value;
    }

    internal void SetTerrain(int x, int y, TileType type)
    {
        if (!IsInside(x, y)) return;
        WorldCellData cell = cells[x, y];
        cell.TerrainType = type;
        cells[x, y] = cell;
    }

    internal void SetFog(int x, int y, FogState state)
    {
        if (!IsInside(x, y)) return;
        WorldCellData cell = cells[x, y];
        cell.FogState = state;
        cells[x, y] = cell;
    }

    internal void SetLiquid(int x, int y, float amount)
    {
        if (!IsInside(x, y)) return;
        WorldCellData cell = cells[x, y];
        cell.LiquidAmount = Mathf.Max(0f, amount);
        cells[x, y] = cell;
    }

    public string GetBiomeId(int x, int y)
    {
        return IsInside(x, y) ? biomeIds[x + y * Width] : null;
    }

    internal void SetBiomeId(int x, int y, string biomeId)
    {
        if (IsInside(x, y)) biomeIds[x + y * Width] = biomeId;
    }

    internal bool TryAddFlora(
        FloraDefinitionSO definition,
        string floraId,
        int x,
        int y)
    {
        if (!IsInside(x, y) || definition == null) return false;

        int cellIndex = x + y * Width;
        if (!floraCells.Add(cellIndex)) return false;

        generatedFlora.Add(new GeneratedFloraData(
            definition,
            string.IsNullOrWhiteSpace(floraId)
                ? definition.floraId
                : floraId,
            new Vector2Int(x, y),
            definition.initialPortions));
        return true;
    }

    internal void RemoveFloraInArea(
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY)
    {
        for (int i = generatedFlora.Count - 1; i >= 0; i--)
        {
            Vector2Int position = generatedFlora[i].Position;
            if (position.x < minimumX || position.x > maximumX
                || position.y < minimumY || position.y > maximumY)
            {
                continue;
            }

            generatedFlora.RemoveAt(i);
            floraCells.Remove(position.x + position.y * Width);
        }
    }

    private bool IsInside(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }
}

public sealed class GeneratedFloraData
{
    public FloraDefinitionSO Definition { get; }
    public string FloraId { get; }
    public Vector2Int Position { get; }
    public int InitialPortions { get; }

    public GeneratedFloraData(
        FloraDefinitionSO definition,
        string floraId,
        Vector2Int position,
        int initialPortions)
    {
        Definition = definition;
        FloraId = floraId;
        Position = position;
        InitialPortions = Mathf.Max(0, initialPortions);
    }
}

public struct WorldCellData
{
    public TileType TerrainType;
    public FogState FogState;
    public float LiquidAmount;
}
