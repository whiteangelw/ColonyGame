using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChestInspectUI : MonoBehaviour
{
    public static ChestInspectUI Instance { get; private set; }

    [Header("UI Elements")]
    [SerializeField] private GameObject panelContainer;
    [SerializeField] private TextMeshProUGUI resourcesText;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button closeButton;

    private Vector2Int currentChestGridPos;

    private void Awake()
    {
        InitializeSingleton();
    }

    private void Start()
    {
        SetupButtonListeners();

        if (panelContainer != null)
        {
            panelContainer.SetActive(false);
        }
    }

    private void OnEnable()
    {
        GameEvents.OnChestUpdated += HandleChestUpdated;
    }

    private void OnDisable()
    {
        GameEvents.OnChestUpdated -= HandleChestUpdated;
    }

    private void InitializeSingleton()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void SetupButtonListeners()
    {
        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeleteClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(ClosePanel);
        }
    }

    public void OpenPanel(Vector2Int gridPos)
    {
        currentChestGridPos = gridPos;

        if (panelContainer != null)
        {
            panelContainer.SetActive(true);
        }

        UpdateResourceList();
    }

    public void ClosePanel()
    {
        if (panelContainer != null && panelContainer.activeSelf)
        {
            panelContainer.SetActive(false);
        }
    }

    private void HandleChestUpdated(Vector2Int gridPos)
    {
        if (panelContainer != null && panelContainer.activeSelf && gridPos == currentChestGridPos)
        {
            UpdateResourceList();
        }
    }

    private void UpdateResourceList()
    {
        if (resourcesText == null) return;

        StorageStructure storage = StructureManager.Instance?.GetStorageAt(currentChestGridPos);
        if (storage == null)
        {
            resourcesText.text = "<b>Conteúdo do Baú:</b>\n<i>(Estrutura não encontrada)</i>";
            return;
        }

        System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
        stringBuilder.AppendLine("<b>Conteúdo do Baú:</b>");

        bool hasItems = false;
        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            int amount = storage.GetLocalAmount(type);
            if (amount > 0)
            {
                stringBuilder.AppendLine($"- {type}: {amount} / {storage.maxCapacityPerResource}");
                hasItems = true;
            }
        }

        if (!hasItems)
        {
            stringBuilder.AppendLine("<i>(Vazio)</i>");
        }

        resourcesText.text = stringBuilder.ToString();
    }

    private void OnDeleteClicked()
    {
        StructureManager.Instance?.DismantleStructureAt(currentChestGridPos);
        ClosePanel();
    }
}