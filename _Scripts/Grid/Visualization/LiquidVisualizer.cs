using UnityEngine;
using UnityEngine.Tilemaps;

public class LiquidVisualizer : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Tilemap liquidTilemap;

    [Header("Assets de Nível da Água")]
    [SerializeField] private TileBase waterLowTile;    // 0.1 a 0.33
    [SerializeField] private TileBase waterMidTile;    // 0.34 a 0.66
    [SerializeField] private TileBase waterFullTile;   // 0.67 a 1.0

    [Header("Cores do Gradiente por Profundidade")]
    [SerializeField] private Color surfaceWaterColor = new Color(0.35f, 0.75f, 1.0f, 0.45f); // Superfície (Claro e Transparente)
    [SerializeField] private Color deepWaterColor = new Color(0.05f, 0.20f, 0.60f, 0.90f);    // Profundo (Escuro e Denso)
    [SerializeField] private int maxDepthForGradient = 8; // Número de blocos de profundidade para atingir a cor mais escura

    [Header("Performance")]
    [SerializeField, Min(0.05f)] private float visualRefreshInterval = 0.2f;

    private GridManager gridManager;
    private bool missingTilemapLogged;
    private float nextVisualRefreshTime;

    private void Start()
    {
        gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();

        if (liquidTilemap == null)
            liquidTilemap = GetComponent<Tilemap>();
    }

    private void LateUpdate()
    {
        if (gridManager == null || !TryGetLiquidTilemap()) return;

        if (Time.unscaledTime < nextVisualRefreshTime) return;
        nextVisualRefreshTime = Time.unscaledTime + visualRefreshInterval;

        UpdateLiquidTiles();
    }

    private void UpdateLiquidTiles()
    {
        int width = gridManager.width;
        int height = gridManager.height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile tile = gridManager.GetTile(x, y);
                Vector3Int tilePos = new Vector3Int(x, y, 0);

                if (tile == null || tile.liquidAmount <= tile.minLiquid)
                {
                    if (liquidTilemap.HasTile(tilePos))
                    {
                        liquidTilemap.SetTile(tilePos, null);
                    }
                    continue;
                }

                // 1. Atualiza o Tile Visual (Formato/Nível)
                TileBase selectedTile = GetWaterTile(tile.liquidAmount);
                if (liquidTilemap.GetTile(tilePos) != selectedTile)
                {
                    liquidTilemap.SetTile(tilePos, selectedTile);
                }

                // 2. Calcula a profundidade vertical (quantos blocos de água há acima deste)
                int depth = GetWaterDepth(x, y);

                // Normaliza a profundidade de 0.0 (superfície) a 1.0 (fundo máximo definido)
                float depthFactor = Mathf.Clamp01((float)depth / maxDepthForGradient);

                // 3. Aplica a cor suave com base na profundidade
                Color dynamicColor = Color.Lerp(surfaceWaterColor, deepWaterColor, depthFactor);

                liquidTilemap.SetTileFlags(tilePos, TileFlags.None);
                liquidTilemap.SetColor(tilePos, dynamicColor);
            }
        }
    }

    // Conta quantos blocos de água contínuos existem acima da posição atual
    private int GetWaterDepth(int startX, int startY)
    {
        int depth = 0;
        int currentY = startY + 1;

        while (currentY < gridManager.height)
        {
            Tile aboveTile = gridManager.GetTile(startX, currentY);
            if (aboveTile != null && aboveTile.liquidAmount > aboveTile.minLiquid)
            {
                depth++;
                currentY++;
            }
            else
            {
                break; // A coluna d'água acabou
            }
        }

        return depth;
    }

    private TileBase GetWaterTile(float amount)
    {
        if (amount < 0.34f) return waterLowTile != null ? waterLowTile : waterFullTile;
        if (amount < 0.67f) return waterMidTile != null ? waterMidTile : waterFullTile;
        return waterFullTile;
    }

    private bool TryGetLiquidTilemap()
    {
        if (liquidTilemap != null) return true;

        liquidTilemap = GetComponent<Tilemap>();
        if (liquidTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning("[LiquidVisualizer] Liquid Tilemap ausente ou destruído; atualização de líquidos ignorada.", this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
