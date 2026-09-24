using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DuplicantAttentionUI : MonoBehaviour
{
    [Header("Interface")]
    [SerializeField] private GameObject alertRoot;
    [SerializeField] private Button alertButton;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text reasonText;

    [Header("Navegação")]
    [SerializeField] private CameraController cameraController;
    [Tooltip("Zoom aplicado ao focalizar um personagem pelo alerta.")]
    [SerializeField, Min(0.1f)] private float focusZoom = 6f;

    private DuplicantAttentionService service;

    private void Awake()
    {
        if (alertRoot == null)
            alertRoot = gameObject;

        if (alertButton == null)
            alertButton = GetComponentInChildren<Button>(true);

        alertButton?.onClick.AddListener(FocusNextAffectedDuplicant);
        SetVisible(false);
    }

    private void Start()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>(
                FindObjectsInactive.Exclude);
        }

        BindService();
        RefreshView();
    }

    private void OnDestroy()
    {
        if (alertButton != null)
            alertButton.onClick.RemoveListener(FocusNextAffectedDuplicant);

        if (service != null)
            service.AttentionChanged -= RefreshView;
    }

    public void FocusNextAffectedDuplicant()
    {
        if (service == null)
            BindService();

        if (service == null
            || !service.TryGetNext(out DuplicantAttentionReport report)
            || report.Source == null)
        {
            RefreshView();
            return;
        }

        if (cameraController != null)
        {
            cameraController.RestoreView(
                report.Source.TargetTransform.position,
                focusZoom);
        }

        DuplicantSelectionTarget target = report.Source.SelectionTarget;
        if (target != null)
            DuplicantSelectionService.Instance?.Select(target, false);
    }

    private void BindService()
    {
        DuplicantAttentionService found =
            DuplicantAttentionService.Instance;

        if (service == found) return;

        if (service != null)
            service.AttentionChanged -= RefreshView;

        service = found;

        if (service != null)
            service.AttentionChanged += RefreshView;
    }

    private void RefreshView()
    {
        if (service == null)
            BindService();

        int count = service != null ? service.ActiveCount : 0;
        SetVisible(count > 0);

        if (countText != null)
            countText.text = count.ToString();

        if (reasonText == null || service == null) return;

        reasonText.text = service.TryGetMostSevere(out DuplicantAttentionReport report)
            ? GetReasonLabel(report.Reason)
            : string.Empty;
    }

    private void SetVisible(bool visible)
    {
        if (alertRoot != null && alertRoot != gameObject)
            alertRoot.SetActive(visible);
        else if (alertButton != null)
            alertButton.gameObject.SetActive(visible);
    }

    private static string GetReasonLabel(DuplicantAttentionReason reason)
    {
        switch (reason)
        {
            case DuplicantAttentionReason.EmergencyResting:
                return "Desmaiado";
            case DuplicantAttentionReason.Starving:
                return "Fome crítica";
            case DuplicantAttentionReason.Exhausted:
                return "Energia crítica";
            case DuplicantAttentionReason.Hungry:
                return "Com fome";
            case DuplicantAttentionReason.Tired:
                return "Cansado";
            default:
                return string.Empty;
        }
    }
}
