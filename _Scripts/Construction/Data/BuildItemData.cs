using UnityEngine;

public enum BuildCategory
{
    Blocks,
    Storage,
    Machines
}

[System.Serializable]
public struct BuildRecipe
{
    [Tooltip("Opcional. Use BuildDefinitionSO para conteúdos novos.")]
    public string definitionId;
    public string displayName;
    public TileType tileType;
    public BuildCategory category;
    [Tooltip("Terrain/Structure são físicos. BackWall e Decoration coexistem com eles.")]
    public GridLayer placementLayer;
    public Sprite icon;
}
