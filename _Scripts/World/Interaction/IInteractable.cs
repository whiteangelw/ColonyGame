using UnityEngine;

public interface IInteractable
{
    Vector2Int GridPosition { get; }
    void OnInteract();
}