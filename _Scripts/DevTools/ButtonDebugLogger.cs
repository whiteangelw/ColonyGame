using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonDebugLogger : MonoBehaviour, IPointerClickHandler
{
    private void Start()
    {
        if (EventSystem.current == null)
        {
            Debug.LogError("<color=red>[CRITICAL ERROR] Nenhum EventSystem foi encontrado na Cena! Adicione um via GameObject > UI > Event System.</color>");
        }

        if (TryGetComponent<Button>(out var btn))
        {
            Debug.Log($"[BUTTON CHECK] Botão '{gameObject.name}' está Interactable? {btn.interactable}");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"<color=cyan>[BUTTON CLICK] O clique físico no botão '{gameObject.name}' foi detectado pelo EventSystem!</color>");
    }
}