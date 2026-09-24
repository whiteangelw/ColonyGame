using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantSelectionTarget))]
public sealed class DuplicantSelectionOutline : MonoBehaviour
{
    private static readonly Vector2[] Directions =
    {
        Vector2.left,
        Vector2.right,
        Vector2.up,
        Vector2.down,
        new Vector2(-1f, -1f).normalized,
        new Vector2(-1f, 1f).normalized,
        new Vector2(1f, -1f).normalized,
        new Vector2(1f, 1f).normalized
    };

    [Header("Referência")]
    [SerializeField] private SpriteRenderer sourceRenderer;

    [Header("Aparência")]
    [SerializeField] private Color outlineColor =
        new Color(0.20f, 0.85f, 1f, 0.95f);
    [SerializeField, Min(0.001f)] private float outlineWidth = 0.025f;
    [SerializeField, Min(1)] private int sortingOrderOffset = 1;

    private DuplicantSelectionTarget selectionTarget;
    private DuplicantSelectionService selectionService;
    private SpriteRenderer[] outlineRenderers;
    private bool isVisible;

    private void Awake()
    {
        selectionTarget = GetComponent<DuplicantSelectionTarget>();

        if (sourceRenderer == null)
            sourceRenderer = GetComponentInChildren<SpriteRenderer>(true);

        CreateOutlineRenderers();
        SetOutlineVisible(false);
    }

    private void OnEnable()
    {
        if (selectionTarget == null)
            selectionTarget = GetComponent<DuplicantSelectionTarget>();

        selectionTarget.SelectionChanged += HandleSelectionChanged;
        BindSelectionService();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        if (selectionTarget != null)
            selectionTarget.SelectionChanged -= HandleSelectionChanged;

        if (selectionService != null)
            selectionService.OverviewChanged -= HandleOverviewChanged;

        isVisible = false;
        SetOutlineVisible(false);
    }

    private void LateUpdate()
    {
        if (!isVisible || sourceRenderer == null) return;
        SynchronizeOutline();
    }

    private void HandleSelectionChanged(bool selected)
    {
        RefreshVisibility();
    }

    private void HandleOverviewChanged(bool enabled)
    {
        RefreshVisibility();
    }

    private void BindSelectionService()
    {
        selectionService = DuplicantSelectionService.Instance;
        if (selectionService != null)
            selectionService.OverviewChanged += HandleOverviewChanged;
    }

    private void RefreshVisibility()
    {
        bool overviewEnabled = selectionService != null
            && selectionService.IsOverviewEnabled;
        isVisible = selectionTarget != null
            && (selectionTarget.IsSelected || overviewEnabled);

        if (isVisible)
            SynchronizeOutline();

        SetOutlineVisible(isVisible);
    }

    private void CreateOutlineRenderers()
    {
        if (sourceRenderer == null)
        {
            Debug.LogWarning(
                "[DuplicantSelection] SpriteRenderer visual não encontrado.",
                this);
            outlineRenderers = new SpriteRenderer[0];
            return;
        }

        outlineRenderers = new SpriteRenderer[Directions.Length];

        for (int i = 0; i < Directions.Length; i++)
        {
            GameObject outlineObject = new GameObject($"SelectionOutline_{i}");
            outlineObject.transform.SetParent(sourceRenderer.transform, false);
            outlineObject.transform.localPosition =
                Directions[i] * outlineWidth;

            SpriteRenderer renderer =
                outlineObject.AddComponent<SpriteRenderer>();
            outlineRenderers[i] = renderer;
        }

        SynchronizeOutline();
    }

    private void SynchronizeOutline()
    {
        if (sourceRenderer == null || outlineRenderers == null) return;

        for (int i = 0; i < outlineRenderers.Length; i++)
        {
            SpriteRenderer renderer = outlineRenderers[i];
            if (renderer == null) continue;

            renderer.sprite = sourceRenderer.sprite;
            renderer.color = outlineColor;
            renderer.flipX = sourceRenderer.flipX;
            renderer.flipY = sourceRenderer.flipY;
            renderer.drawMode = sourceRenderer.drawMode;
            renderer.size = sourceRenderer.size;
            renderer.sharedMaterial = sourceRenderer.sharedMaterial;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder =
                sourceRenderer.sortingOrder - sortingOrderOffset;
            renderer.maskInteraction = sourceRenderer.maskInteraction;

            renderer.transform.localPosition =
                Directions[i] * outlineWidth;
        }
    }

    private void SetOutlineVisible(bool visible)
    {
        if (outlineRenderers == null) return;

        foreach (SpriteRenderer renderer in outlineRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    private void OnValidate()
    {
        outlineWidth = Mathf.Max(0.001f, outlineWidth);
        sortingOrderOffset = Mathf.Max(1, sortingOrderOffset);

        if (Application.isPlaying && isVisible)
            SynchronizeOutline();
    }
}
