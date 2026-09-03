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
    public string displayName;
    public TileType tileType;
    public BuildCategory category;
    public Sprite icon;
}