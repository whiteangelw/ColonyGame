using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TerrainDefinition",
    menuName = "World/Terrain/Terrain Definition")]
public sealed class TerrainDefinitionSO : ScriptableObject
{
    [Header("Identidade")]
    [SerializeField] private TileType tileType = TileType.Solid;
    [SerializeField] private string displayName;

    [Header("Drops ao escavar")]
    [SerializeField] private List<TerrainDropRule> drops =
        new List<TerrainDropRule>();

    public TileType TileType => tileType;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName.Trim();
    public IReadOnlyList<TerrainDropRule> Drops => drops;

    public void RollDrops(
        List<TerrainDropResult> results,
        float chanceMultiplier = 1f,
        float amountMultiplier = 1f)
    {
        if (results == null) return;

        for (int i = 0; i < drops.Count; i++)
        {
            TerrainDropRule rule = drops[i];
            if (rule != null && rule.TryRoll(
                out TerrainDropResult result,
                chanceMultiplier,
                amountMultiplier))
                MergeResult(results, result);
        }
    }

    private static void MergeResult(
        List<TerrainDropResult> results,
        TerrainDropResult incoming)
    {
        for (int i = 0; i < results.Count; i++)
        {
            TerrainDropResult current = results[i];
            if (current.ResourceType != incoming.ResourceType) continue;

            results[i] = new TerrainDropResult(
                current.ResourceType,
                current.Amount + incoming.Amount);
            return;
        }

        results.Add(incoming);
    }

    private void OnValidate()
    {
        if (drops == null || drops.Count == 0)
        {
            Debug.LogWarning(
                $"[TerrainDefinitionSO] {name} não possui drops. Isso é " +
                "válido, mas o terreno não produzirá recursos.",
                this);
            return;
        }

        HashSet<ResourceType> seenResources = new HashSet<ResourceType>();

        for (int i = 0; i < drops.Count; i++)
        {
            TerrainDropRule rule = drops[i];
            if (rule == null) continue;

            rule.Validate();
            if (!seenResources.Add(rule.ResourceType))
            {
                Debug.LogWarning(
                    $"[TerrainDefinitionSO] {name} possui o recurso " +
                    $"{rule.ResourceType} repetido. Os resultados serão somados.",
                    this);
            }
        }
    }
}
