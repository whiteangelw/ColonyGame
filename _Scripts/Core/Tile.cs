using UnityEngine;

public enum TileType
{
    Empty,
    Solid,      // Terra comum
    Grass,      // Grama na superfície
    Stone,      // Pedra
    Copper,     // Minério de Cobre
    Coal,       // Carvão
    Iron,       // Ferro
    Gold,       // Ouro
    Ladder,     // Escada
    Chest,
    Bedrock
}

public enum FogState
{
    Unexplored, // 100% Escuro (Preto)
    Explored,   // 50% Transparente
    Revealed    // 100% Visível
}

public class Tile
{
    public int x;
    public int y;
    public TileType type;
    public bool isPassable;
    public FogState fogState = FogState.Unexplored;

    // --- NOVO CAMPO PARA ACESSIBILIDADE ---
    public int reachabilityGroupID = -1; // -1 significa inalcançável/não atribuído

    // --- CAMPOS PARA FLUIDOS ---
    public float liquidAmount = 0f;
    public float minLiquid = 0.001f;

    public Tile(int x, int y, TileType type)
    {
        this.x = x;
        this.y = y;
        this.type = type;
        this.isPassable = (type == TileType.Empty || type == TileType.Ladder);
    }
}