using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // Importante para o New Input System
using TMPro;

public class PriorityUIPanel : MonoBehaviour
{
    [System.Serializable]
    public struct CategoryUIRow
    {
        public TaskType type;
        public TextMeshProUGUI priorityText;
        public Button increaseButton;
        public Button decreaseButton;
    }

    [Header("Configurações do Painel")]
    [SerializeField] private GameObject panelContainer;
    [SerializeField] private Button hudToggleButton;

    [Header("Linhas da UI de Categoria")]
    [SerializeField] private CategoryUIRow[] categoryRows;

    private void Start()
    {
        SetupButtons();
        UpdateUI();

        if (hudToggleButton != null)
            hudToggleButton.onClick.AddListener(TogglePanel);
    }

    private void Update()
    {
        if (InputContextService.Instance != null
            && !InputContextService.Instance.IsGameplayKeyboardAllowed)
        {
            return;
        }

        // Pressionar a tecla 'P' usando o New Input System
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            TogglePanel();
        }
    }

    public void TogglePanel()
    {
        if (panelContainer != null)
        {
            bool isActive = !panelContainer.activeSelf;
            panelContainer.SetActive(isActive);

            if (isActive)
            {
                UpdateUI();
            }
        }
    }

    private void SetupButtons()
    {
        foreach (var row in categoryRows)
        {
            TaskType type = row.type;

            if (row.increaseButton != null)
                row.increaseButton.onClick.AddListener(() => ChangePriority(type, 1));

            if (row.decreaseButton != null)
                row.decreaseButton.onClick.AddListener(() => ChangePriority(type, -1));
        }
    }

    private void ChangePriority(TaskType type, int delta)
    {
        if (PriorityManager.Instance == null) return;

        int currentPriority = PriorityManager.Instance.GetCategoryPriority(type);
        int newPriority = Mathf.Clamp(currentPriority + delta, 1, 9);

        PriorityManager.Instance.SetCategoryPriority(type, newPriority);
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (PriorityManager.Instance == null) return;

        foreach (var row in categoryRows)
        {
            int priority = PriorityManager.Instance.GetCategoryPriority(row.type);
            if (row.priorityText != null)
            {
                row.priorityText.text = priority.ToString();
            }
        }
    }
}
