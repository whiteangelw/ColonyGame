using UnityEngine;

[DisallowMultipleComponent]
public class DuplicantSaveIdentity : MonoBehaviour
{
    [SerializeField] private string definitionId;

    public string DefinitionId => definitionId;

    public void SetDefinitionId(string value)
    {
        definitionId = string.IsNullOrWhiteSpace(value)
            ? "__fallback__"
            : value;
    }
}
