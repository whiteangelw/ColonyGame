using UnityEngine;

[CreateAssetMenu(
    fileName = "NewDuplicantDefinition",
    menuName = "Duplicants/Duplicant Definition"
)]
public class DuplicantDefinition : ScriptableObject
{
    [Header("Identidade")]
    public string displayName;

    [Min(0.01f)]
    public float selectionWeight = 1f;

    [Header("Prefab e perfis")]
    public GameObject prefab;
    public DuplicantWorkProfile workProfile;
    public DuplicantCapabilityProfile capabilityProfile;
    public DuplicantLifeProfile lifeProfile;
}
