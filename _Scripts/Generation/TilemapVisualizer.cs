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
        if (!TryGetTargetTilemap()) return;

        Vector3Int tilePosition = new Vector3Int(x, y, 0);

        if (tileDictionary.TryGetValue(newType, out TileBase tileAsset))
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
                TileType type = GridManager.Instance.GetTileType(x, y);
                Vector3Int pos = new Vector3Int(x, y, 0);

                if (tileDictionary.TryGetValue(type, out TileBase tileAsset))
                    targetTilemap.SetTile(pos, tileAsset);
            }
        }
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
