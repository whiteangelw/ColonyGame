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
    [TextArea(2, 4)] [SerializeField] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private TileBase tileAsset;
    [Tooltip("Menor valor aparece primeiro no menu. Empates usam nome e Definition Id.")]
    [SerializeField] private int menuSortOrder;

    [Header("Estrutura física (opcional)")]
    [Tooltip("Prefab instanciado quando a construção termina. Vazio para tiles sem GameObject.")]
    [SerializeField] private GameObject prefab;

    [Header("Requisitos de conteúdo")]
    [Tooltip("Exige um prefab para esta definição. Usado pelo validador do Editor.")]
    [SerializeField] private bool requiresPhysicalPrefab;
    [Tooltip("Exige um Tile Asset para esta definição. Usado pelo validador do Editor.")]
    [SerializeField] private bool requiresTileAsset;

    [Header("Interação")]
    [SerializeField] private bool canBeDismantled = true;

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
        new StructureFootprintDefinition
        {
            // Prefabs físicos bloqueiam passagem por padrão, mas só servem
            // de piso quando o asset declarar isso explicitamente.
            supportsWeight = false
        };

    public string DefinitionId => definitionId?.Trim();
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName;
    public string Description => description?.Trim() ?? string.Empty;
    public Sprite Icon => icon;
    public TileBase TileAsset => tileAsset;
    public int MenuSortOrder => menuSortOrder;
    public GameObject Prefab => prefab;
    public bool HasPhysicalPrefab => prefab != null;
    public bool RequiresPhysicalPrefab => requiresPhysicalPrefab;
    public bool RequiresTileAsset => requiresTileAsset;
    public bool CanBeDismantled => canBeDismantled;
    public TileType TileType => tileType;
    public GridLayer PlacementLayer => placementLayer;
    public BuildCategory Category => category;
    public ResourceType RequiredResource => requiredResource;
    public int Cost => Mathf.Max(1, cost);
    public int RefundAmount => Mathf.FloorToInt(Cost * Mathf.Clamp01(refundRate));
    public float WorkTime => Mathf.Max(0.1f, workTime);
    public StructureFootprintDefinition Footprint => footprint;

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
