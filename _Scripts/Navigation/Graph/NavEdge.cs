using Unity.AI.Navigation.LowLevel;
using UnityEngine;

[System.Serializable]
public class NavEdge
{
    public NavNode targetNode;      // Nó de destino
    public MovementType moveType;  // Walk, StepUp, JumpGap, ClimbLadder, etc.
    public float cost;              // Custo total calculado para percorrer este trecho

    public NavEdge(NavNode targetNode, MovementType moveType, float cost)
    {
        this.targetNode = targetNode;
        this.moveType = moveType;
        this.cost = cost;
    }
}