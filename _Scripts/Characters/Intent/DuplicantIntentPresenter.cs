using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantIntentSensor))]
public sealed class DuplicantIntentPresenter : MonoBehaviour
{
    [SerializeField] private DuplicantIntentPhraseCatalogSO phraseCatalog;
    [SerializeField] private bool showIntentMessages = true;

    private DuplicantIntentSensor sensor;
    private float nextAllowedMessageTime;
    private string previousPhrase;

    private void Awake()
    {
        sensor = GetComponent<DuplicantIntentSensor>();
    }

    private void OnEnable()
    {
        if (sensor != null) sensor.IntentRaised += HandleIntentRaised;
    }

    private void OnDisable()
    {
        if (sensor != null) sensor.IntentRaised -= HandleIntentRaised;
    }

    private void HandleIntentRaised(DuplicantIntentSignal signal)
    {
        if (!showIntentMessages || phraseCatalog == null) return;

        DuplicantIntentPhraseSet phraseSet = phraseCatalog.Get(signal.Type);
        if (phraseSet == null || !phraseSet.enabled) return;

        if (!phraseSet.bypassGlobalCooldown
            && Time.time < nextAllowedMessageTime)
        {
            return;
        }

        string phrase = phraseSet.GetRandomPhrase(previousPhrase);
        if (string.IsNullOrWhiteSpace(phrase)) return;

        previousPhrase = phrase;
        nextAllowedMessageTime = Time.time
            + Mathf.Max(0f, phraseCatalog.minimumSecondsBetweenMessages);

        GameEvents.TriggerFloatingTextRequested(
            phrase,
            transform.position + phraseCatalog.worldOffset,
            phraseSet.color);
    }
}
