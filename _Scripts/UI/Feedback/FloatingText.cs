using TMPro;
using UnityEngine;

public class FloatingText : MonoBehaviour
{
    [Header("Movimento")]
    [SerializeField, Min(0f)] private float moveSpeed = 0.45f;

    [Header("Leitura")]
    [Tooltip("Ativa o efeito de revelar a mensagem letra por letra.")]
    [SerializeField] private bool useTypewriterEffect = true;
    [Tooltip("Quantidade de caracteres revelados por segundo.")]
    [SerializeField, Min(1f)] private float charactersPerSecond = 35f;
    [Tooltip("Tempo mínimo que a frase fica inteira e visível antes de sumir.")]
    [SerializeField, Min(0f)] private float minimumVisibleDuration = 1.25f;
    [Tooltip("Tempo adicional de leitura para cada caractere da frase.")]
    [SerializeField, Min(0f)] private float visibleSecondsPerCharacter = 0.025f;

    [Header("Desaparecimento")]
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.45f;

    private TextMeshPro textMesh;
    private Color textColor;
    private string fullText = string.Empty;
    private float elapsed;
    private float typingDuration;
    private float visibleDuration;
    private float totalDuration;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
    }

    private void OnEnable()
    {
        elapsed = 0f;
    }

    public void Setup(string text, Color color)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();

        fullText = text ?? string.Empty;
        textColor = color;
        elapsed = 0f;

        typingDuration = useTypewriterEffect && fullText.Length > 0
            ? fullText.Length / Mathf.Max(1f, charactersPerSecond)
            : 0f;

        visibleDuration = minimumVisibleDuration
            + fullText.Length * visibleSecondsPerCharacter;
        totalDuration = typingDuration + visibleDuration + fadeDuration;

        textMesh.text = typingDuration > 0f ? string.Empty : fullText;
        textMesh.color = textColor;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        transform.position += Vector3.up * moveSpeed * Time.deltaTime;

        UpdateVisibleText();
        UpdateAlpha();

        if (elapsed >= totalDuration)
        {
            ReturnToPool();
        }
    }

    private void UpdateVisibleText()
    {
        if (typingDuration <= 0f || elapsed >= typingDuration)
        {
            textMesh.text = fullText;
            return;
        }

        int visibleCharacters = Mathf.Clamp(
            Mathf.FloorToInt(elapsed * charactersPerSecond),
            0,
            fullText.Length);

        textMesh.text = fullText.Substring(0, visibleCharacters);
    }

    private void UpdateAlpha()
    {
        float fadeStartTime = typingDuration + visibleDuration;
        float alpha = elapsed <= fadeStartTime
            ? 1f
            : Mathf.Lerp(
                1f,
                0f,
                (elapsed - fadeStartTime) / fadeDuration);

        textMesh.color = new Color(
            textColor.r,
            textColor.g,
            textColor.b,
            alpha);
    }

    private void ReturnToPool()
    {
        if (FloatingTextManager.Instance != null)
        {
            FloatingTextManager.Instance.ReturnToPool(this);
            return;
        }

        gameObject.SetActive(false);
    }
}
