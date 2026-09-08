using UnityEngine;
using TMPro;

public class DayNightUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timeText;

    private void OnEnable()
    {
        GameEvents.OnTimeUpdated += OnTimeChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnTimeUpdated -= OnTimeChanged;
    }

    private void OnTimeChanged(int day, string timeStr, string period)
    {
        if (timeText != null)
        {
            timeText.text = $"Dia {day} | {timeStr} ({period})";
        }
    }
}