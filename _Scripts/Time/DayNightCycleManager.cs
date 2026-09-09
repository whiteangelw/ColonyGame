using UnityEngine;

public class DayNightCycleManager : MonoBehaviour
{
    public static DayNightCycleManager Instance { get; private set; }

    public enum TimeOfDay { Night, Morning, Afternoon, Evening }

    [Header("Configurações do Relógio")]
    [Tooltip("Duração total de 1 dia em segundos reais")]
    [SerializeField] private float dayDurationInSeconds = 300f;
    [Range(0f, 1f)]
    [SerializeField] private float currentTimeRatio = 0.5f;

    [Header("Estado Atual")]
    [SerializeField] private int currentDay = 1;
    [SerializeField] private TimeOfDay currentTimeState;

    public float CurrentTimeRatio => currentTimeRatio;
    public TimeOfDay CurrentTimeState => currentTimeState;
    public int CurrentDay => currentDay;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (SaveGameRuntime.IsLoading)
        {
            return;
        }

        AdvanceTime();
        CheckTimeState();
        NotifyTimeUpdate();
    }

    private void AdvanceTime()
    {
        if (dayDurationInSeconds <= 0f) return;

        currentTimeRatio += Time.deltaTime / dayDurationInSeconds;

        if (currentTimeRatio >= 1.0f)
        {
            currentTimeRatio -= 1.0f;
            currentDay++;
        }
    }

    private void CheckTimeState()
    {
        TimeOfDay newState;

        if (currentTimeRatio >= 0.25f && currentTimeRatio < 0.5f)
            newState = TimeOfDay.Morning;
        else if (currentTimeRatio >= 0.5f && currentTimeRatio < 0.75f)
            newState = TimeOfDay.Afternoon;
        else if (currentTimeRatio >= 0.75f && currentTimeRatio < 0.85f)
            newState = TimeOfDay.Evening;
        else
            newState = TimeOfDay.Night;

        if (newState != currentTimeState)
        {
            currentTimeState = newState;
        }
    }

    private void NotifyTimeUpdate()
    {
        int hours = Mathf.FloorToInt(currentTimeRatio * 24f);
        int minutes = Mathf.FloorToInt((currentTimeRatio * 24f - hours) * 60f);
        string timeString = $"{hours:00}:{minutes:00}";
        string periodName = currentTimeState.ToString();

        GameEvents.TriggerTimeUpdated(currentDay, timeString, periodName);
    }

    public DayNightCycleSaveData CaptureState()
    {
        return new DayNightCycleSaveData
        {
            hasState = true,
            currentDay = Mathf.Max(1, currentDay),
            currentTimeRatio = Mathf.Repeat(currentTimeRatio, 1f)
        };
    }

    public void RestoreState(DayNightCycleSaveData savedState)
    {
        if (savedState == null || !savedState.hasState)
        {
            NotifyTimeUpdate();
            return;
        }

        currentDay = Mathf.Max(1, savedState.currentDay);
        currentTimeRatio = Mathf.Repeat(savedState.currentTimeRatio, 1f);
        CheckTimeState();
        NotifyTimeUpdate();
    }
}
