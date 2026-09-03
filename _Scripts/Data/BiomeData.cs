using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class OreConfig
{
    public string name = "Minério";
    public TileType tileType;
    [Range(0f, 1f)] public float threshold = 0.75f; // Quanto maior, mais raro
    public float frequency = 0.12f;                 // Menor = jazidas maiores
    public int minDepth = 0;                        // Profundidade relativa ao bioma (min)
    public int maxDepth = 50;                       // Profundidade relativa ao bioma (max)
}

[CreateAssetMenu(fileName = "NewBiomeData", menuName = "World/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("Identificação")]
    public string biomeName = "Novo Bioma";

    [Header("Blocos Padrão do Bioma")]
    public TileType fillTile = TileType.Stone;     // Bloco principal (ex: Pedra ou Terra)
    public TileType surfaceTile = TileType.Grass;  // Bloco no topo exposto (ex: Grama)

    [Header("Cavernas neste Bioma")]
    public float caveScale = 0.07f;
    [Range(0f, 1f)]
    public float caveThreshold = 0.47f;            // Quanto menor, mais escavado

    [Header("Tabela de Minérios")]
    public List<OreConfig> ores = new List<OreConfig>();
}