using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(
    fileName = "PlanterSettings",
    menuName = "Colony/Structures/Planter Settings")]
public sealed class PlanterSettingsSO : ScriptableObject
{
    [Header("Cultivo")]
    [SerializeField] private FloraDefinitionSO cropDefinition;
    [SerializeField] private List<FloraDefinitionSO> availableCrops =
        new List<FloraDefinitionSO>();
    [SerializeField, Min(0.1f)] private float growthDuration = 45f;
    [SerializeField] private bool startPlanted = true;
    [SerializeField] private bool autoReplant = true;

    [Header("Visual provisório")]
    [SerializeField] private Sprite growingSprite;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Vector3 cropLocalPosition = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private Vector2 interactionSize = new Vector2(0.8f, 1f);

    [Header("Ordem de renderização")]
    [Tooltip("Sorting Layer do conjunto vaso + cultivo. Deve ficar acima de BackWall.")]
    [SerializeField] private string structureSortingLayer = "Structures";
    [Tooltip("Ordem do conjunto completo dentro da Sorting Layer.")]
    [SerializeField] private int structureOrderInLayer;
    [Tooltip("Ordem do cultivo dentro do conjunto. O vaso normalmente usa 0.")]
    [SerializeField] private int cropOrderInGroup = 1;

    public FloraDefinitionSO CropDefinition => cropDefinition;
    public IReadOnlyList<FloraDefinitionSO> AvailableCrops => availableCrops;

    public FloraDefinitionSO GetDefaultCrop()
    {
        if (cropDefinition != null) return cropDefinition;

        foreach (FloraDefinitionSO crop in availableCrops)
        {
            if (crop != null) return crop;
        }

        return null;
    }

    public bool AllowsCrop(FloraDefinitionSO crop)
    {
        if (crop == null) return false;
        if (crop == cropDefinition) return true;
        return availableCrops.Contains(crop);
    }
    public float GrowthDuration => Mathf.Max(0.1f, growthDuration);
    public bool StartPlanted => startPlanted;
    public bool AutoReplant => autoReplant;
    public Sprite GrowingSprite => growingSprite;
    public Sprite ReadySprite => readySprite;
    public Vector3 CropLocalPosition => cropLocalPosition;
    public string StructureSortingLayer =>
        string.IsNullOrWhiteSpace(structureSortingLayer)
            ? "Default"
            : structureSortingLayer.Trim();
    public int StructureOrderInLayer => structureOrderInLayer;
    public int CropOrderInGroup => cropOrderInGroup;
    public Vector2 InteractionSize => new Vector2(
        Mathf.Max(0.1f, interactionSize.x),
        Mathf.Max(0.1f, interactionSize.y));

    public float GetGrowthDuration(FloraDefinitionSO crop)
    {
        return crop != null && crop.planterGrowthDuration > 0f
            ? crop.planterGrowthDuration
            : GrowthDuration;
    }

    public Sprite GetGrowingSprite(FloraDefinitionSO crop)
    {
        return crop != null && crop.planterGrowingSprite != null
            ? crop.planterGrowingSprite
            : growingSprite;
    }

    public Sprite GetReadySprite(FloraDefinitionSO crop)
    {
        if (crop != null && crop.planterReadySprite != null)
        {
            return crop.planterReadySprite;
        }

        if (crop != null && crop.prefab != null)
        {
            SpriteRenderer renderer =
                crop.prefab.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                return renderer.sprite;
            }
        }

        return readySprite;
    }

    private void OnValidate()
    {
        growthDuration = Mathf.Max(0.1f, growthDuration);
        interactionSize.x = Mathf.Max(0.1f, interactionSize.x);
        interactionSize.y = Mathf.Max(0.1f, interactionSize.y);
        if (string.IsNullOrWhiteSpace(structureSortingLayer))
        {
            structureSortingLayer = "Default";
        }
    }
}
