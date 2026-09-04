using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    [SerializeField] private float keyboardSpeed = 15f;

    [Header("Configurações de Zoom")]
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float minZoom = 3f;
    [SerializeField] private float maxZoom = 25f;

    [Header("Limites do Mapa")]
    [SerializeField] private bool useLimits = true;
    [SerializeField] private float padding = 2f;

    private Camera cam;
    private Vector3 dragOrigin;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        HandleKeyboardMovement();
        HandleMouseDrag();
        HandleZoom();
        ClampPosition();
    }

    private void HandleKeyboardMovement()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1;
        }

        Vector3 move = new Vector3(input.normalized.x, input.normalized.y, 0) * (keyboardSpeed * Time.deltaTime);
        transform.position += move;
    }

    private void HandleMouseDrag()
    {
        if (Mouse.current == null) return;

        // Botão do Meio (Scroll Click)
        if (Mouse.current.middleButton.wasPressedThisFrame)
        {
            dragOrigin = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }

        if (Mouse.current.middleButton.isPressed)
        {
            Vector3 currentMouseWorld = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector3 difference = dragOrigin - currentMouseWorld;
            transform.position += difference;
        }
    }

    private void HandleZoom()
    {
        if (Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            float zoomDelta = Mathf.Sign(scroll) * zoomSpeed;
            cam.orthographicSize -= zoomDelta;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
    }

    private void ClampPosition()
    {
        if (!useLimits || GridManager.Instance == null) return;

        float mapWidth = GridManager.Instance.width * GridManager.Instance.cellSize;
        float mapHeight = GridManager.Instance.height * GridManager.Instance.cellSize;

        float minX = -padding;
        float maxX = mapWidth + padding;
        float minY = -padding;
        float maxY = mapHeight + padding;

        Vector3 currentPos = transform.position;
        currentPos.x = Mathf.Clamp(currentPos.x, minX, maxX);
        currentPos.y = Mathf.Clamp(currentPos.y, minY, maxY);

        transform.position = currentPos;
    }

    public void FocusOnPosition(Vector3 worldPos)
    {
        transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        ClampPosition();
    }

    public void FocusOnTargets(IReadOnlyList<Transform> targets)
    {
        if (targets == null || targets.Count == 0)
        {
            return;
        }

        Vector3 center = Vector3.zero;
        int validTargets = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null)
            {
                continue;
            }

            center += targets[i].position;
            validTargets++;
        }

        if (validTargets == 0)
        {
            return;
        }

        FocusOnPosition(center / validTargets);
    }
}
