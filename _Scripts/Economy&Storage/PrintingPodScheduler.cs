using UnityEngine;

public class PrintingPodScheduler : MonoBehaviour
{
    public static PrintingPodScheduler Instance { get; private set; }

    [Header("Temporizador próprio dos drops")]
    [Min(1f)]
    [SerializeField] private float firstOfferDelay = 60f;

    [Min(1f)]
    [SerializeField] private float offerInterval = 300f;

    public float TimeRemaining { get; private set; }
    public bool IsOfferReady { get; private set; }
    public bool IsTimerRunning { get; private set; }
    public bool HasGeneratedFirstOffer => hasGeneratedFirstOffer;

    private int lastDisplayedSecond = -1;
    private bool hasGeneratedFirstOffer;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        GameEvents.OnPrintingPodBuilt += HandlePrintingPodBuilt;
        GameEvents.OnPrintingPodRemoved += HandlePrintingPodRemoved;
    }

    private void Start()
    {
        if (PrintingPod.Instance != null
            && PrintingPod.Instance.IsOperational)
        {
            StartTimer();
        }
        else
        {
            PublishState();
        }
    }

    private void OnDisable()
    {
        GameEvents.OnPrintingPodBuilt -= HandlePrintingPodBuilt;
        GameEvents.OnPrintingPodRemoved -= HandlePrintingPodRemoved;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!IsTimerRunning || IsOfferReady)
        {
            return;
        }

        TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);

        int displayedSecond = Mathf.CeilToInt(TimeRemaining);

        if (displayedSecond != lastDisplayedSecond)
        {
            lastDisplayedSecond = displayedSecond;
            PublishState();
        }

        if (TimeRemaining <= 0f)
        {
            IsTimerRunning = false;
            IsOfferReady = true;
            hasGeneratedFirstOffer = true;
            PublishState();
            GameEvents.TriggerRewardOfferReady();
        }
    }

    public void NotifyOfferClaimed()
    {
        if (!IsOfferReady)
        {
            return;
        }

        IsOfferReady = false;

        if (PrintingPod.Instance != null
            && PrintingPod.Instance.IsOperational)
        {
            StartTimer();
        }
        else
        {
            PublishState();
        }
    }

    public void RestoreState(
        float timeRemaining,
        bool isOfferReady,
        bool isTimerRunning,
        bool generatedFirstOffer)
    {
        TimeRemaining = Mathf.Max(0f, timeRemaining);
        IsOfferReady = isOfferReady;
        IsTimerRunning = isTimerRunning && !isOfferReady;
        hasGeneratedFirstOffer = generatedFirstOffer;
        lastDisplayedSecond = -1;
        PublishState();

        if (IsOfferReady)
        {
            GameEvents.TriggerRewardOfferReady();
        }
    }

    private void HandlePrintingPodBuilt(PrintingPod printingPod)
    {
        if (!IsOfferReady)
        {
            StartTimer();
        }
    }

    private void HandlePrintingPodRemoved(PrintingPod printingPod)
    {
        IsTimerRunning = false;
        IsOfferReady = false;
        TimeRemaining = 0f;
        lastDisplayedSecond = -1;
        PublishState();
    }

    private void StartTimer()
    {
        TimeRemaining = hasGeneratedFirstOffer
            ? offerInterval
            : firstOfferDelay;

        IsTimerRunning = true;
        lastDisplayedSecond = -1;
        PublishState();
    }

    private void PublishState()
    {
        GameEvents.TriggerPrintingPodTimerChanged(
            TimeRemaining,
            IsOfferReady
        );
    }
}
