using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(DuplicantController))]
public sealed class DuplicantSelectionTarget : MonoBehaviour, IInteractable
{
    private DuplicantController controller;

    public Vector2Int GridPosition => controller != null
        ? controller.gridPosition
        : Vector2Int.zero;
    public bool IsSelected { get; private set; }
    public event Action<bool> SelectionChanged;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
    }

    public void OnInteract()
    {
        DuplicantSelectionService service = DuplicantSelectionService.Instance;
        if (service == null)
        {
            Debug.LogWarning(
                "[DuplicantSelection] DuplicantSelectionService não encontrado.",
                this);
            return;
        }

        service.Select(this, IsAdditiveSelectionPressed());
    }

    public void SetSelectedFromService(bool selected)
    {
        if (IsSelected == selected) return;
        IsSelected = selected;
        SelectionChanged?.Invoke(selected);
    }

    private void OnDisable()
    {
        DuplicantSelectionService.Instance?.Deselect(this);
    }

    private static bool IsAdditiveSelectionPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;

        return keyboard.leftCtrlKey.isPressed
            || keyboard.rightCtrlKey.isPressed
            || keyboard.leftShiftKey.isPressed
            || keyboard.rightShiftKey.isPressed;
    }
}
