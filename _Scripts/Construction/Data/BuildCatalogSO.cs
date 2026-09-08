using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "BuildCatalog",
    menuName = "Colony/Construction/Build Catalog")]
public sealed class BuildCatalogSO : ScriptableObject
{
    [SerializeField] private List<BuildDefinitionSO> definitions =
        new List<BuildDefinitionSO>();

    public IReadOnlyList<BuildDefinitionSO> Definitions => definitions;
}
