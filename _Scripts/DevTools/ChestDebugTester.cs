using UnityEngine;

public class ChestDebugTester : MonoBehaviour
{
    private void OnMouseDown()
    {
        Debug.Log("<color=green>[SUCCESS] OnMouseDown do Unity detectou o clique direto no Baú!</color>");

        if (TryGetComponent<StorageStructure>(out var storage))
        {
            Debug.Log($"[STORAGE] Invocando OnInteract() manualmente via OnMouseDown na posição {storage.GridPosition}...");
            storage.OnInteract();
        }
        else
        {
            Debug.LogError("[ERROR] Componente StorageStructure NÃO encontrado no GameObject do Baú!");
        }
    }
}