using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DayNightLightingController : MonoBehaviour
{
    [Header("Referências Visuais")]
    [SerializeField] private Light2D globalLight;
    [SerializeField] private Camera mainCamera;

    [Header("Configurações de Cor e Luz")]
    [SerializeField] private Gradient ambientColorGradient;
    [SerializeField] private AnimationCurve lightIntensityCurve = AnimationCurve.EaseInOut(0, 0.3f, 1, 0.3f);

    private void Update()
    {
        if (DayNightCycleManager.Instance == null) return;

        float ratio = DayNightCycleManager.Instance.CurrentTimeRatio;
        UpdateLighting(ratio);
    }

    private void UpdateLighting(float ratio)
    {
        Color currentColor = ambientColorGradient.Evaluate(ratio);

        if (globalLight != null)
        {
            globalLight.color = currentColor;
            globalLight.intensity = lightIntensityCurve.Evaluate(ratio);
        }

        if (mainCamera != null)
        {
            mainCamera.backgroundColor = currentColor;
        }
    }
}