using System.Collections.Generic;
using UnityEngine;

public class NavNode
{
    public Vector2Int gridPosition;
    public List<NavEdge> connections;

    // Construtor
    public NavNode(int x, int y)
    {
        this.gridPosition = new Vector2Int(x, y);
        this.connections = new List<NavEdge>();
    }

    public void AddConnection(NavNode targetNode, MovementType moveType, float cost)
    {
        // Evita duplicatas para o mesmo destino
        if (!connections.Exists(e => e.targetNode == targetNode))
        {
            connections.Add(new NavEdge(targetNode, moveType, cost));
        }
    }

    public void ClearConnections()
    {
        connections.Clear();
    }
}