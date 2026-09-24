using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantVitals))]
[RequireComponent(typeof(DuplicantSelectionTarget))]
public sealed class DuplicantStatusStrip : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private GameObject stripRoot;
    [SerializeField] private Image energyFill;
    [SerializeField] private Image hungerFill;

    [Header("Transição")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.30f;

    [Header("Cores da energia")]
    [SerializeField] private Color energyNormalColor =
        new Color(0.20f, 0.65f, 1f);
    [SerializeField] private Color energyWarningColor =
        new Color(1f, 0.75f, 0.20f);
    [SerializeField] private Color energyCriticalColor =
        new Color(0.95f, 0.25f, 0.20f);

    [Header("Cores da fome")]
    [SerializeField] private Color hungerNormalColor =
        new Color(0.30f, 0.85f, 0.45f);
    [SerializeField] private Color hungerWarningColor =
        new Color(1f, 0.50f, 0.12f);
    [SerializeField] private Color hungerCriticalColor =
        new Color(0.80f, 0.15f, 0.35f);

    [Header("Limites de alerta")]
    [SerializeField, Range(0f, 1f)] private float warningBelow = 0.35f;
    [SerializeField, Range(0f, 1f)] private float criticalBelow = 0.10f;

    private DuplicantVitals vitals;
    private DuplicantSelectionTarget selectionTarget;
    private DuplicantSelectionService selectionService;
    private Coroutine energyTransition;
    private Coroutine hungerTransition;

    private void Awake()
    {
        vitals = GetComponent<DuplicantVitals>();
        selectionTarget = GetComponent<DuplicantSelectionTarget>();
        DisableRaycasts();
        SetVisible(false);
    }

    private void OnEnable()
    {
        if (vitals == null) vitals = GetComponent<DuplicantVitals>();
        if (selectionTarget == null)
            selectionTarget = GetComponent<DuplicantSelectionTarget>();

        vitals.OnVitalsChanged += HandleVitalsChanged;
        selectionTarget.SelectionChanged += HandleSelectionChanged;
        BindSelectionService();
        RefreshImmediate();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        StopTransitions();

        if (vitals != null)
            vitals.OnVitalsChanged -= HandleVitalsChanged;

        if (selectionTarget != null)
            selectionTarget.SelectionChanged -= HandleSelectionChanged;

        if (selectionService != null)
            selectionService.OverviewChanged -= HandleOverviewChanged;
    }

    private void HandleSelectionChanged(bool selected)
    {
        if (selected) RefreshImmediate();
        RefreshVisibility();
    }

    private void HandleVitalsChanged(DuplicantVitals changedVitals)
    {
        if (changedVitals == vitals && ShouldBeVisible())
            RefreshAnimated();
    }

    private void HandleOverviewChanged(bool enabled)
    {
        if (enabled) RefreshImmediate();
        RefreshVisibility();
    }

    private void BindSelectionService()
    {
        selectionService = DuplicantSelectionService.Instance;
        if (selectionService != null)
            selectionService.OverviewChanged += HandleOverviewChanged;
    }

    private bool ShouldBeVisible()
    {
        bool overviewEnabled = selectionService != null
            && selectionService.IsOverviewEnabled;
        return selectionTarget != null
            && (selectionTarget.IsSelected || overviewEnabled);
    }

    private void RefreshVisibility()
    {
        SetVisible(ShouldBeVisible());
    }

    private void RefreshImmediate()
    {
        if (vitals == null) return;

        StopTransitions();

        SetFillImmediate(
            energyFill,
            vitals.EnergyPercent,
            energyNormalColor,
            energyWarningColor,
            energyCriticalColor);

        SetFillImmediate(
            hungerFill,
            vitals.HungerPercent,
            hungerNormalColor,
            hungerWarningColor,
            hungerCriticalColor);
    }

    private void RefreshAnimated()
    {
        if (vitals == null) return;

        if (energyTransition != null)
            StopCoroutine(energyTransition);
        if (hungerTransition != null)
            StopCoroutine(hungerTransition);

        energyTransition = StartCoroutine(AnimateFill(
            energyFill,
            vitals.EnergyPercent,
            energyNormalColor,
            energyWarningColor,
            energyCriticalColor,
            () => energyTransition = null));

        hungerTransition = StartCoroutine(AnimateFill(
            hungerFill,
            vitals.HungerPercent,
            hungerNormalColor,
            hungerWarningColor,
            hungerCriticalColor,
            () => hungerTransition = null));
    }

    private void SetFillImmediate(
        Image image,
        float percent,
        Color normalColor,
        Color warningColor,
        Color criticalColor)
    {
        if (image == null) return;

        float normalized = Mathf.Clamp01(percent);
        SetNormalizedWidth(image.rectTransform, normalized);
        image.color = GetStatusColor(
            normalized,
            normalColor,
            warningColor,
            criticalColor);
    }

    private IEnumerator AnimateFill(
        Image image,
        float targetPercent,
        Color normalColor,
        Color warningColor,
        Color criticalColor,
        System.Action onCompleted)
    {
        if (image == null)
        {
            onCompleted?.Invoke();
            yield break;
        }

        float target = Mathf.Clamp01(targetPercent);
        float start = GetNormalizedWidth(image.rectTransform);

        if (transitionDuration <= 0f || Mathf.Approximately(start, target))
        {
            SetFillImmediate(
                image,
                target,
                normalColor,
                warningColor,
                criticalColor);
            onCompleted?.Invoke();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / transitionDuration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
            float current = Mathf.Lerp(start, target, smoothProgress);

            SetFillImmediate(
                image,
                current,
                normalColor,
                warningColor,
                criticalColor);
            yield return null;
        }

        SetFillImmediate(
            image,
            target,
            normalColor,
            warningColor,
            criticalColor);
        onCompleted?.Invoke();
    }

    private static float GetNormalizedWidth(RectTransform rectTransform)
    {
        return rectTransform != null
            ? Mathf.Clamp01(rectTransform.anchorMax.x)
            : 0f;
    }

    private static void SetNormalizedWidth(
        RectTransform rectTransform,
        float normalizedWidth)
    {
        if (rectTransform == null) return;

        float width = Mathf.Clamp01(normalizedWidth);
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(width, 1f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private void StopTransitions()
    {
        if (energyTransition != null)
        {
            StopCoroutine(energyTransition);
            energyTransition = null;
        }

        if (hungerTransition != null)
        {
            StopCoroutine(hungerTransition);
            hungerTransition = null;
        }
    }

    private Color GetStatusColor(
        float percent,
        Color normalColor,
        Color warningColor,
        Color criticalColor)
    {
        if (percent <= criticalBelow) return criticalColor;
        if (percent <= warningBelow) return warningColor;
        return normalColor;
    }

    private void SetVisible(bool visible)
    {
        if (stripRoot != null)
            stripRoot.SetActive(visible);
    }

    private void DisableRaycasts()
    {
        if (energyFill != null) energyFill.raycastTarget = false;
        if (hungerFill != null) hungerFill.raycastTarget = false;
    }

    private void OnValidate()
    {
        warningBelow = Mathf.Clamp01(warningBelow);
        criticalBelow = Mathf.Clamp(criticalBelow, 0f, warningBelow);
        transitionDuration = Mathf.Max(0f, transitionDuration);
        DisableRaycasts();
    }
}
