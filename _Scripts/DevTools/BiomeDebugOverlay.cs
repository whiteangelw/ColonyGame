using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BiomeDebugColor
{
    public BiomeDefinitionSO biome;
    public Color color = Color.magenta;
}

[DisallowMultipleComponent]
public sealed class BiomeDebugOverlay : MonoBehaviour
{
    [Header("Controle")]
    [SerializeField] private bool visibleOnStart;
    [SerializeField] private KeyCode toggleKey = KeyCode.F8;
    [SerializeField, Range(0.05f, 1f)] private float opacity = 0.42f;
    [SerializeField] private bool showLegend = true;

    [Header("Cores opcionais")]
    [SerializeField] private List<BiomeDebugColor> colors =
        new List<BiomeDebugColor>();

    [Header("Renderização")]
    [SerializeField] private int sortingOrder = 30000;
    [SerializeField] private float worldZ = -0.1f;

    private readonly Dictionary<string, Color32> colorByBiomeId =
        new Dictionary<string, Color32>(StringComparer.Ordinal);
    private readonly List<string> discoveredBiomeIds = new List<string>();

    private GridManager subscribedGrid;
    private SpriteRenderer overlayRenderer;
    private Texture2D overlayTexture;
    private Sprite overlaySprite;
    private GUIStyle legendStyle;

    private void Awake()
    {
        CreateRenderer();
        RebuildColorLookup();
        SetVisible(visibleOnStart);
    }

    private void Start()
    {
        SubscribeToGrid();
        if (subscribedGrid != null && subscribedGrid.IsGridReady)
        {
            Rebuild(subscribedGrid.LastGenerationResult);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            SetVisible(!overlayRenderer.enabled);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromGrid();
        if (overlaySprite != null) Destroy(overlaySprite);
        if (overlayTexture != null) Destroy(overlayTexture);
    }

    private void OnGUI()
    {
        if (!showLegend || overlayRenderer == null
            || !overlayRenderer.enabled || discoveredBiomeIds.Count == 0)
        {
            return;
        }

        if (legendStyle == null)
        {
            legendStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                normal = { textColor = Color.white }
            };
        }

        const float width = 230f;
        const float lineHeight = 24f;
        float panelHeight = 36f + discoveredBiomeIds.Count * lineHeight;
        GUI.Box(new Rect(12f, 12f, width, panelHeight), "Biomas (F8)");

        for (int i = 0; i < discoveredBiomeIds.Count; i++)
        {
            string biomeId = discoveredBiomeIds[i];
            Color color = colorByBiomeId[biomeId];
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 1f);
            GUI.Box(new Rect(22f, 43f + i * lineHeight, 18f, 18f), string.Empty);
            GUI.color = previous;
            GUI.Label(
                new Rect(46f, 40f + i * lineHeight, 185f, 22f),
                biomeId,
                legendStyle);
        }
    }

    [ContextMenu("Rebuild Overlay")]
    public void RebuildCurrentWorld()
    {
        SubscribeToGrid();
        Rebuild(subscribedGrid != null
            ? subscribedGrid.LastGenerationResult
            : null);
    }

    public void SetVisible(bool visible)
    {
        if (overlayRenderer != null) overlayRenderer.enabled = visible;
    }

    private void Rebuild(WorldGenerationResult result)
    {
        ReleaseVisualResources();
        discoveredBiomeIds.Clear();
        if (result == null) return;

        int width = result.Width;
        int height = result.Height;
        Color32[] pixels = new Color32[width * height];
        HashSet<string> discovered = new HashSet<string>(StringComparer.Ordinal);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                string biomeId = result.GetBiomeId(x, y);
                if (string.IsNullOrEmpty(biomeId)) continue;

                Color32 color = ResolveColor(biomeId);
                color.a = (byte)Mathf.RoundToInt(opacity * 255f);
                pixels[x + y * width] = color;
                discovered.Add(biomeId);
            }
        }

        discoveredBiomeIds.AddRange(discovered);
        discoveredBiomeIds.Sort(StringComparer.Ordinal);

        overlayTexture = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "BiomeDebugOverlayTexture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        overlayTexture.SetPixels32(pixels);
        overlayTexture.Apply(false, true);

        float cellSize = GridManager.Instance != null
            ? Mathf.Max(0.0001f, GridManager.Instance.cellSize)
            : 1f;
        overlaySprite = Sprite.Create(
            overlayTexture,
            new Rect(0f, 0f, width, height),
            Vector2.zero,
            1f / cellSize,
            0,
            SpriteMeshType.FullRect);
        overlaySprite.name = "BiomeDebugOverlaySprite";
        overlayRenderer.sprite = overlaySprite;
        overlayRenderer.transform.position = new Vector3(0f, 0f, worldZ);
    }

    private void CreateRenderer()
    {
        GameObject visual = new GameObject("Biome Overlay Visual");
        visual.transform.SetParent(transform, false);
        overlayRenderer = visual.AddComponent<SpriteRenderer>();
        overlayRenderer.sortingOrder = sortingOrder;
    }

    private void RebuildColorLookup()
    {
        colorByBiomeId.Clear();
        for (int i = 0; i < colors.Count; i++)
        {
            BiomeDebugColor entry = colors[i];
            if (entry == null || entry.biome == null
                || string.IsNullOrWhiteSpace(entry.biome.biomeId))
            {
                continue;
            }

            colorByBiomeId[entry.biome.biomeId] = entry.color;
        }
    }

    private Color32 ResolveColor(string biomeId)
    {
        if (colorByBiomeId.TryGetValue(biomeId, out Color32 color))
        {
            return color;
        }

        uint hash = 2166136261u;
        for (int i = 0; i < biomeId.Length; i++)
        {
            hash ^= biomeId[i];
            hash *= 16777619u;
        }

        float hue = (hash % 1000u) / 1000f;
        color = Color.HSVToRGB(hue, 0.72f, 1f);
        colorByBiomeId.Add(biomeId, color);
        return color;
    }

    private void SubscribeToGrid()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || grid == subscribedGrid) return;

        UnsubscribeFromGrid();
        subscribedGrid = grid;
        subscribedGrid.OnWorldGenerated += Rebuild;
    }

    private void UnsubscribeFromGrid()
    {
        if (subscribedGrid == null) return;
        subscribedGrid.OnWorldGenerated -= Rebuild;
        subscribedGrid = null;
    }

    private void ReleaseVisualResources()
    {
        if (overlayRenderer != null) overlayRenderer.sprite = null;
        if (overlaySprite != null) Destroy(overlaySprite);
        if (overlayTexture != null) Destroy(overlayTexture);
        overlaySprite = null;
        overlayTexture = null;
    }
}
