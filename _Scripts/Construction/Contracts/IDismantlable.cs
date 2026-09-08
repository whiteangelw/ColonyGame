using UnityEngine;

public interface IDismantlable
{
    Vector2Int GridPosition { get; }
    void Dismantle();
}