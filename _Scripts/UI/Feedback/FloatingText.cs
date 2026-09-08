using TMPro;
using UnityEngine;

public class FloatingText : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float fadeDuration = 1.0f;

    private TextMeshPro textMesh;
    private Color textColor;
    private float elapsed = 0f;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
    }

    private void OnEnable()
    {
        // Reseta o contador sempre que o texto sai da pool
        elapsed = 0f;
    }

    public void Setup(string text, Color color)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();

        textMesh.text = text;
        textMesh.color = color;
        textColor = color;
    }

    private void Update()
    {
        // Sobe o texto no eixo Y
        transform.position += Vector3.up * moveSpeed * Time.deltaTime;

        // Reduz a opacidade gradualmente
        elapsed += Time.deltaTime;
        float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
        textMesh.color = new Color(textColor.r, textColor.g, textColor.b, alpha);

        // Devolve ao Pool em vez de destruir
        if (elapsed >= fadeDuration)
        {
            if (FloatingTextManager.Instance != null)
            {
                FloatingTextManager.Instance.ReturnToPool(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}