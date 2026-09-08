using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(
    fileName = "BuildDefinition",
    menuName = "Colony/Construction/Build Definition")]
public sealed class BuildDefinitionSO : ScriptableObject
{
    [Header("Identidade persistente")]
    [SerializeField] private string definitionId;

    [Header("Apresentação")]
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private TileBase tileAsset;

    [Header("Construção")]
    [SerializeField] private TileType tileType = TileType.Solid;
    [SerializeField] private GridLayer placementLayer = GridLayer.Terrain;
    [SerializeField] private BuildCategory category = BuildCategory.Blocks;
    [SerializeField] private ResourceType requiredResource = ResourceType.Dirt;
    [SerializeField, Min(1)] private int cost = 1;
    [SerializeField, Range(0f, 1f)] private float refundRate = 1f;
    [SerializeField, Min(0.1f)] private float workTime = 3f;

    [Header("Footprint")]
    [SerializeField] private StructureFootprintDefinition footprint =
        new StructureFootprintDefinition();

    public string DefinitionId => definitionId?.Trim();
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName;
    public Sprite Icon => icon;
    public TileBase TileAsset => tileAsset;
    public TileType TileType => tileType;
    public GridLayer PlacementLayer => placementLayer;
    public BuildCategory Category => category;
    public ResourceType RequiredResource => requiredResource;
    public int Cost => Mathf.Max(1, cost);
    public int RefundAmount => Mathf.FloorToInt(Cost * Mathf.Clamp01(refundRate));
    public float WorkTime => Mathf.Max(0.1f, workTime);

    public StructureFootprintDefinition GetFootprint()
    {
        if (footprint == null)
        {
            return StructureFootprintSettings.Resolve(tileType);
        }

        footprint.tileType = tileType;
        footprint.width = Mathf.Max(1, footprint.width);
        footprint.height = Mathf.Max(1, footprint.height);
        return footprint;
    }

    private void OnValidate()
    {
        cost = Mathf.Max(1, cost);
        workTime = Mathf.Max(0.1f, workTime);

        if (footprint != null)
        {
            footprint.tileType = tileType;
            footprint.width = Mathf.Max(1, footprint.width);
            footprint.height = Mathf.Max(1, footprint.height);
        }
    }
}
