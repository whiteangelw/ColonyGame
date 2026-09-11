using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Profiling;

public class FogOfWarManager : Singleton<FogOfWarManager>
{
    private static readonly ProfilerMarker FullRefreshMarker =
        new ProfilerMarker("FogOfWar.FullRefresh");
    private static readonly ProfilerMarker FullRefreshBatchMarker =
        new ProfilerMarker("FogOfWar.FullRefreshBatch");

    [Header("Referências")]
    [SerializeField] private Tilemap fogTilemap;
    [SerializeField] private TileBase blackFogTile; // Tile Escuro (100% Opaco)
    [SerializeField] private TileBase semiFogTile;  // Tile Semitransparente (Degradê)

    [Header("Carregamento incremental")]
    [SerializeField, Min(1)] private int fullRefreshColumnsPerFrame = 16;

    [Header("Configurações do Raio de Visão")]
    [SerializeField] private int innerRadius = 4; // Raio 100% Visível
    [SerializeField] private int outerRadius = 7; // Raio do Degradê/Sombra

    private GridManager subscribedGridManager;
    private bool missingTilemapLogged;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        subscribedGridManager = GridManager.Instance;
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnGridRebuilt += HandleGridRebuilt;

            if (subscribedGridManager.IsGridReady)
            {
                InitializeFog();
            }
        }
    }

    private void OnDestroy()
    {
        if (subscribedGridManager != null)
        {
            subscribedGridManager.OnGridRebuilt -= HandleGridRebuilt;
        }
    }

    private void HandleGridRebuilt()
    {
        if (!SaveGameRuntime.IsLoading)
        {
            InitializeFog();
        }
    }

    public IEnumerator InitializeFogIncrementally()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !TryGetFogTilemap()) yield break;

        fogTilemap.ClearAllTiles();
        int columnsPerFrame = Mathf.Max(1, fullRefreshColumnsPerFrame);

        for (int startX = 0; startX < grid.width; startX += columnsPerFrame)
        {
            int endX = Mathf.Min(startX + columnsPerFrame, grid.width);
            using (FullRefreshBatchMarker.Auto())
            {
                for (int x = startX; x < endX; x++)
                {
                    for (int y = 0; y < grid.height; y++)
                    {
                        Tile tile = grid.GetTile(x, y);
                        if (tile != null)
                        {
                            UpdateFogTileVisual(x, y, tile.fogState);
                        }
                    }
                }
            }

            if (endX < grid.width) yield return null;
        }
    }

    public void InitializeFog()
    {
        if (GridManager.Instance == null || !TryGetFogTilemap()) return;

        long startedAt = PerformanceMetricsService.BeginSample();
        using (FullRefreshMarker.Auto())
        {

        fogTilemap.ClearAllTiles();

        int width = GridManager.Instance.width;
        int height = GridManager.Instance.height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile tile = GridManager.Instance.GetTile(x, y);
                if (tile != null)
                {
                    UpdateFogTileVisual(x, y, tile.fogState);
                }
            }
        }
        }
        PerformanceMetricsService.EndSample(
            PerformanceMetric.FogFullRefresh,
            startedAt);
    }

    public void RevealArea(Vector2Int centerGridPos)
    {
        if (GridManager.Instance == null || !TryGetFogTilemap()) return;

        for (int x = -outerRadius; x <= outerRadius; x++)
        {
            for (int y = -outerRadius; y <= outerRadius; y++)
            {
                float distance = Mathf.Sqrt(x * x + y * y);

                if (distance <= outerRadius)
                {
                    int targetX = centerGridPos.x + x;
                    int targetY = centerGridPos.y + y;

                    Tile tile = GridManager.Instance.GetTile(targetX, targetY);
                    if (tile == null) continue;
                    FogState previousState = tile.fogState;

                    if (distance <= innerRadius)
                    {
                        // Visão total
                        tile.fogState = FogState.Revealed;
                    }
                    else if (tile.fogState != FogState.Revealed)
                    {
                        // Borda em Degradê (Só vira Explored se não tiver sido 100% revelado antes)
                        tile.fogState = FogState.Explored;
                    }

                    if (tile.fogState != previousState)
                    {
                        GridManager.Instance.MarkRegionDirty(
                            targetX,
                            targetY,
                            WorldRegionDirtyFlags.Fog
                            | WorldRegionDirtyFlags.Visual);
                    }

                    UpdateFogTileVisual(targetX, targetY, tile.fogState);
                }
            }
        }
    }

    private void UpdateFogTileVisual(int x, int y, FogState state)
    {
        if (!TryGetFogTilemap()) return;

        Vector3Int pos = new Vector3Int(x, y, 0);

        switch (state)
        {
            case FogState.Unexplored:
                fogTilemap.SetTile(pos, blackFogTile);
                break;
            case FogState.Explored:
                fogTilemap.SetTile(pos, semiFogTile != null ? semiFogTile : blackFogTile);
                break;
            case FogState.Revealed:
                fogTilemap.SetTile(pos, null); // Remove a névoa para mostrar o mapa normal
                break;
        }
    }

    private bool TryGetFogTilemap()
    {
        if (fogTilemap != null) return true;

        fogTilemap = GetComponent<Tilemap>();
        if (fogTilemap != null)
        {
            missingTilemapLogged = false;
            return true;
        }

        if (!missingTilemapLogged)
        {
            Debug.LogWarning("[FogOfWarManager] Fog Tilemap ausente ou destruído; atualização de névoa ignorada.", this);
            missingTilemapLogged = true;
        }

        return false;
    }
}
