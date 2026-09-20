using UnityEngine;

[RequireComponent(typeof(CreatureController), typeof(SpriteRenderer))]
public sealed class CreaturePresentation : MonoBehaviour
{
    [Header("Leitura visual")]
    [SerializeField] private float contentBobAmount = 0.045f;
    [SerializeField] private float contentBobFrequency = 7f;
    [SerializeField] private float uncomfortableShakeAmount = 0.04f;
    [SerializeField] private float uncomfortableShakeFrequency = 13f;
    [SerializeField] private float neglectedDroopAmount = 0.08f;
    [SerializeField] private Color contentTint = new Color(0.82f, 1f, 0.78f);
    [SerializeField] private Color uncomfortableTint = new Color(1f, 0.72f, 0.48f);
    [SerializeField] private Color neglectedTint = new Color(0.68f, 0.78f, 1f);
    [SerializeField] private Color eatingTint = new Color(1f, 0.95f, 0.62f);

    private CreatureController controller;
    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale;
    private Color baseColor;

    private void Awake()
    {
        controller = GetComponent<CreatureController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        baseColor = spriteRenderer.color;
    }

    private void OnDisable() => RestoreBaseAppearance();

    private void Update()
    {
        if (controller == null || spriteRenderer == null) return;

        CreatureRelationshipMood mood = controller.Domestication != null
            ? controller.Domestication.Mood
            : CreatureRelationshipMood.Wild;
        float time = Time.time;
        Vector3 scale = baseScale;
        Color tint = baseColor;

        if (controller.State == CreatureState.Eating)
        {
            float bite = 1f + Mathf.Sin(time * 10f) * 0.06f;
            scale = new Vector3(baseScale.x * bite, baseScale.y / bite, baseScale.z);
            tint = Color.Lerp(baseColor, eatingTint, 0.45f);
        }
        else if (mood == CreatureRelationshipMood.Content)
        {
            float bob = Mathf.Sin(time * contentBobFrequency) * contentBobAmount;
            scale = new Vector3(baseScale.x * (1f - bob * 0.45f),
                baseScale.y * (1f + bob), baseScale.z);
            tint = Color.Lerp(baseColor, contentTint, 0.28f);
        }
        else if (mood == CreatureRelationshipMood.Overfed
            || mood == CreatureRelationshipMood.Refusing)
        {
            float shake = Mathf.Sin(time * uncomfortableShakeFrequency)
                * uncomfortableShakeAmount;
            scale = new Vector3(baseScale.x * (1f + shake),
                baseScale.y * (1f - shake), baseScale.z);
            tint = Color.Lerp(baseColor, uncomfortableTint, 0.5f);
        }
        else if (controller.Domestication != null
            && controller.Domestication.ShouldSignalNeglect)
        {
            scale = new Vector3(baseScale.x * (1f + neglectedDroopAmount * 0.5f),
                baseScale.y * (1f - neglectedDroopAmount), baseScale.z);
            tint = Color.Lerp(baseColor, neglectedTint, 0.45f);
        }

        transform.localScale = scale;
        spriteRenderer.color = tint;
    }

    private void RestoreBaseAppearance()
    {
        transform.localScale = baseScale;
        if (spriteRenderer != null) spriteRenderer.color = baseColor;
    }
}
