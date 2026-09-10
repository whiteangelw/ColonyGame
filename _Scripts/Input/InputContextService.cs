using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Decide se o teclado pertence ao gameplay ou à edição de texto da UI.
/// Qualquer TMP_InputField focado entra automaticamente no contexto de texto.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class InputContextService : MonoBehaviour
{
    public static InputContextService Instance { get; private set; }

    private TMP_InputField activeTextField;
    private int suppressGameplayKeyboardFrame = -1;

    public bool IsTextEditing
    {
        get
        {
            RefreshActiveTextField();
            return activeTextField != null;
        }
    }

    public bool IsGameplayKeyboardAllowed
    {
        get
        {
            return !IsTextEditing
                && suppressGameplayKeyboardFrame != Time.frameCount;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        RefreshActiveTextField();

        if (activeTextField != null
            && Keyboard.current != null
            && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Impede que o mesmo Escape seja reutilizado por um atalho global.
            suppressGameplayKeyboardFrame = Time.frameCount;
            activeTextField.DeactivateInputField();

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            activeTextField = null;
        }
    }

    private void OnDisable()
    {
        activeTextField = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void RefreshActiveTextField()
    {
        if (EventSystem.current == null)
        {
            activeTextField = null;
            return;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        TMP_InputField inputField = selected != null
            ? selected.GetComponentInParent<TMP_InputField>()
            : null;

        activeTextField = inputField != null
            && inputField.isActiveAndEnabled
            && inputField.isFocused
            ? inputField
            : null;
    }
}
