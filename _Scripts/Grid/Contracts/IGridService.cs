using UnityEngine;

public interface IGridService
{
    int Width { get; }
    int Height { get; }

    Tile GetTile(int x, int y);
    Tile GetTile(Vector2Int gridPos);
    bool IsPassable(int x, int y);
    Vector2Int WorldToGridPosition(Vector3 worldPos);
}