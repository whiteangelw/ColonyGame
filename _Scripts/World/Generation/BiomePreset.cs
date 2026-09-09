using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewWorldPreset", menuName = "World/World Preset")]
public class WorldPreset : ScriptableObject
{
    public string presetName = "Mundo Padrão";
    public List<WorldGenerator.BiomeLayer> biomeLayers = new List<WorldGenerator.BiomeLayer>();
}