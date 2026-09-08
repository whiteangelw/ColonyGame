using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapVisualizer : MonoBehaviour
{
    [System.Serializable]
    public struct TileMapping
    {
        public TileType tileType;
        public TileBase tileAsset;
    }

    [Header("Referências")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private GridLayer visualizedLayer = GridLayer.Terrain;

    [Header("Mapeamento de Tiles")]
    [SerializeField] private List<TileMapping> mappingList = new List<TileMapping>();

    private Dictionary<TileType, TileBase> tileDictionary;
    private GridManager subscribedGridManager;
    private bool missingTilemapLogged;

    private void Awake()
    {
        if (targetTilemap == null)
            targetTilemap = GetComponent<Tilemap>();

        InitializeDictionary();
    }

    private void Start()
    {
        subscribedGridManager = GridManager.Instance;

        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnTileChanged += OnTileChangedHandler;
            subscribedGridManager.OnLayerTileChanged += OnLayerTileChangedHandler;
            subscribedGridManager.OnGridRebuilt += RenderFullGrid;

            // Se o grid já estiver pronto quando o Start rodar, desenha imediatamente
            if (subscribedGridManager.IsGridReady)
            {
                RenderFullGrid();
            }
        }
    }

    private void OnDestroy()
    {
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnTileChanged -= OnTileChangedHandler;
            subscribedGridManager.OnLayerTileChanged -= OnLayerTileChangedHandler;
            subscribedGridManager.OnGridRebuilt -= RenderFullGrid;
        }
    }

    private void InitializeDictionary()
    {
        tileDictionary = new Dictionary<TileType, TileBase>();
        foreach (var mapping in mappingList)
        {
            if (mapping.tileAsset != null && !tileDictionary.ContainsKey(mapping.tileType))
                tileDictionary.Add(mapping.tileType, mapping.tileAsset);
        }
    }

    private void OnTileChangedHandler(int x, int y, TileType newType)
    {
        if (visualizedLayer != GridLayer.Terrain && visualizedLayer != GridLayer.Structure) return;
        RenderCell(x, y, GridManager.Instance.GetTileType(x, y, visualizedLayer));
    }

    private void OnLayerTileChangedHandler(int x, int y, GridLayer layer, TileType newType)
    {
        if (layer != visualizedLayer) return;
        RenderCell(x, y, newType);
    }

    private void RenderCell(int x, int y, TileType newType)
    {
        if (!TryGetTargetTilemap()) return;

        Vector3Int tilePosition = new Vector3Int(x, y, 0);

        if (TryResolveTileAsset(x, y, newType, out TileBase tileAsset))
            targetTilemap.SetTile(tilePosition, tileAsset);
        else
            targetTilemap.SetTile(tilePosition, null);
    }

    public void RenderFullGrid()
    {
        if (GridManager.Instance == null || !TryGetTargetTilemap()) return;

        targetTilemap.ClearAllTiles();

        for (int x = 0; x < GridManager.Instance.width; x++)
        {
            for (int y = 0; y < GridManager.Instance.height; y++)
            {
                TileType type = GridManager.Instance.GetTileType(x, y, visualizedLayer);
                Vector3Int pos = new Vector3Int(x, y, 0);

                if (TryResolveTileAsset(x, y, type, out TileBase tileAsset))
                    targetTilemap.SetTile(pos, tileAsset);
            }
        }
    }

    private bool TryResolveTileAsset(
        int x,
        int y,
        TileType fallbackType,
        out TileBase tileAsset)
    {
        tileAsset = null;

        string contentId = GridManager.Instance?.GetContentId(
            x,
            y,
            visualizedLayer);
        BuildDefinitionSO definition =
            BuildCatalogService.Instance?.GetById(contentId);

        if (definition != null
            && definition.PlacementLayer == visualizedLayer
            && definition.TileAsset != null)
        {
            tileAsset = definition.TileAsset;
            return true;
        }

        return tileDictionary.TryGetValue(fallbackType, out tileAsset);
    }

    private bool TryGetTargetTilemap()
    {
        if (targetTilemap != null) return true;

        targetTilemap = GetComponent<Tilemap>();
        if (targetTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning("[TilemapVisualizer] Tilemap ausente ou destruído; atualização visual ignorada.", this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
