using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-900)]
public class GameSessionMenuUI : MonoBehaviour
{
    [Header("Tela inicial")]
    [SerializeField] private GameObject startupPanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button newGameButton;

    [Header("Slots de save")]
    [SerializeField] private Button slot1Button;
    [SerializeField] private Button slot2Button;
    [SerializeField] private Button slot3Button;
    [SerializeField] private Text slot1Text;
    [SerializeField] private Text slot2Text;
    [SerializeField] private Text slot3Text;

    [Header("Confirmação de novo jogo")]
    [SerializeField] private GameObject newGameConfirmationPanel;
    [SerializeField] private Button confirmNewGameButton;
    [SerializeField] private Button cancelNewGameButton;

    [Header("HUD")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Text statusText;

    [Header("Controles pausados durante o menu")]
    [Tooltip("Arraste aqui PlayerInput e CameraController.")]
    [SerializeField] private Behaviour[] gameplayBehavioursToDisable;

    private SaveGameService saveService;
    private float timeScaleBeforeMenu = 1f;
    private bool startupMenuOpen;

    private void Awake()
    {
        saveService = SaveGameService.Instance;

        if (saveService == null)
        {
            Debug.LogError("[GameSessionMenuUI] SaveGameService não encontrado.");
            enabled = false;
            return;
        }

        BindButtons();
        saveService.OnStartupSessionFinished += HandleStartupSessionFinished;
        saveService.OnSaveFinished += HandleSaveFinished;
        saveService.OnLoadProgress += HandleLoadProgress;
        saveService.RequireStartupChoice();
        SelectSlot(1);
        OpenStartupMenu();
    }

    private void OnDestroy()
    {
        UnbindButtons();

        if (saveService != null)
        {
            saveService.OnStartupSessionFinished -= HandleStartupSessionFinished;
            saveService.OnSaveFinished -= HandleSaveFinished;
            saveService.OnLoadProgress -= HandleLoadProgress;
        }

        if (startupMenuOpen)
        {
            SetGameplayEnabled(true);
            Time.timeScale = timeScaleBeforeMenu;
        }
    }

    private void BindButtons()
    {
        continueButton?.onClick.AddListener(ContinueGame);
        newGameButton?.onClick.AddListener(RequestNewGame);
        confirmNewGameButton?.onClick.AddListener(ConfirmNewGame);
        cancelNewGameButton?.onClick.AddListener(CancelNewGame);
        saveButton?.onClick.AddListener(SaveGame);
        slot1Button?.onClick.AddListener(SelectSlot1);
        slot2Button?.onClick.AddListener(SelectSlot2);
        slot3Button?.onClick.AddListener(SelectSlot3);
    }

    private void UnbindButtons()
    {
        continueButton?.onClick.RemoveListener(ContinueGame);
        newGameButton?.onClick.RemoveListener(RequestNewGame);
        confirmNewGameButton?.onClick.RemoveListener(ConfirmNewGame);
        cancelNewGameButton?.onClick.RemoveListener(CancelNewGame);
        saveButton?.onClick.RemoveListener(SaveGame);
        slot1Button?.onClick.RemoveListener(SelectSlot1);
        slot2Button?.onClick.RemoveListener(SelectSlot2);
        slot3Button?.onClick.RemoveListener(SelectSlot3);
    }

    private void OpenStartupMenu()
    {
        startupMenuOpen = true;
        timeScaleBeforeMenu = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        SetGameplayEnabled(false);

        startupPanel?.SetActive(true);
        newGameConfirmationPanel?.SetActive(false);

        if (continueButton != null)
        {
            continueButton.interactable = saveService.HasSave;
        }

        if (saveButton != null)
        {
            saveButton.interactable = false;
        }

        SetStatus(saveService.LastOperationMessage);
        RefreshSlotTexts();
    }

    private void SelectSlot1() => SelectSlot(1);
    private void SelectSlot2() => SelectSlot(2);
    private void SelectSlot3() => SelectSlot(3);

    private void SelectSlot(int slot)
    {
        if (!saveService.SelectSlot(slot))
        {
            return;
        }

        SetStatus(saveService.LastOperationMessage);
        RefreshSlotTexts();
        SetMenuButtonsInteractable(true);
    }

    private void RefreshSlotTexts()
    {
        SetSlotText(slot1Text, 1);
        SetSlotText(slot2Text, 2);
        SetSlotText(slot3Text, 3);
    }

    private void SetSlotText(Text label, int slot)
    {
        if (label == null)
        {
            return;
        }

        string selectedMarker = saveService.ActiveSlot == slot ? "> " : string.Empty;
        label.text = selectedMarker + saveService.GetSlotDescription(slot);
    }

    private void ContinueGame()
    {
        SetMenuButtonsInteractable(false);
        SetStatus("Carregando colônia...");

        if (!saveService.ContinueFromStartupMenu())
        {
            SetMenuButtonsInteractable(true);
            SetStatus(saveService.LastOperationMessage);
        }
    }

    private void RequestNewGame()
    {
        if (saveService.HasSave && newGameConfirmationPanel != null)
        {
            newGameConfirmationPanel.SetActive(true);
            return;
        }

        ConfirmNewGame();
    }

    private void ConfirmNewGame()
    {
        newGameConfirmationPanel?.SetActive(false);
        SetMenuButtonsInteractable(false);
        SetStatus("Criando nova colônia...");

        if (!saveService.StartNewGameFromStartupMenu(true))
        {
            SetMenuButtonsInteractable(true);
            SetStatus(saveService.LastOperationMessage);
        }
    }

    private void CancelNewGame()
    {
        newGameConfirmationPanel?.SetActive(false);
    }

    private void SaveGame()
    {
        if (saveService.IsBusy) return;

        if (saveButton != null) saveButton.interactable = false;
        saveService.SaveGame();
        SetStatus(saveService.LastOperationMessage);
    }

    private void HandleSaveFinished(bool succeeded, string message)
    {
        SetStatus(message);
        RefreshSlotTexts();

        if (saveButton != null)
        {
            saveButton.interactable = true;
        }
    }

    private void HandleLoadProgress(float progress, string message)
    {
        SetStatus(message);
    }

    private void HandleStartupSessionFinished(bool succeeded, string message)
    {
        SetStatus(message);

        if (!succeeded)
        {
            SetMenuButtonsInteractable(true);
            return;
        }

        startupMenuOpen = false;
        startupPanel?.SetActive(false);
        newGameConfirmationPanel?.SetActive(false);
        Time.timeScale = timeScaleBeforeMenu;
        SetGameplayEnabled(true);

        if (saveButton != null)
        {
            saveButton.interactable = true;
        }
    }

    private void SetMenuButtonsInteractable(bool interactable)
    {
        if (continueButton != null)
        {
            continueButton.interactable = interactable && saveService.HasSave;
        }

        if (newGameButton != null)
        {
            newGameButton.interactable = interactable;
        }
    }

    private void SetGameplayEnabled(bool value)
    {
        if (gameplayBehavioursToDisable == null)
        {
            return;
        }

        foreach (Behaviour behaviour in gameplayBehavioursToDisable)
        {
            if (behaviour != null)
            {
                behaviour.enabled = value;
            }
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
