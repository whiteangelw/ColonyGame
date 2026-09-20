using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ProductionRecipe",
    menuName = "Colony/Production/Recipe")]
public sealed class ProductionRecipeSO : ScriptableObject
{
    [Serializable]
    public sealed class Ingredient
    {
        public ResourceType resourceType;
        [Min(1)] public int amount = 1;
    }

    [SerializeField] private string recipeId;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField, Min(0.1f)] private float workRequiredPerBatch = 5f;
    [SerializeField] private List<Ingredient> inputs = new List<Ingredient>();
    [SerializeField] private List<Ingredient> outputs = new List<Ingredient>();

    public string RecipeId => recipeId?.Trim() ?? string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public float WorkRequiredPerBatch => Mathf.Max(0.1f, workRequiredPerBatch);
    public IReadOnlyList<Ingredient> Inputs => inputs;
    public IReadOnlyList<Ingredient> Outputs => outputs;

    public int GetInputAmount(ResourceType type)
    {
        int total = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            Ingredient input = inputs[i];
            if (input != null && input.resourceType == type)
            {
                total += Mathf.Max(0, input.amount);
            }
        }
        return total;
    }

    private void OnValidate()
    {
        workRequiredPerBatch = Mathf.Max(0.1f, workRequiredPerBatch);
        Normalize(inputs);
        Normalize(outputs);
    }

    private static void Normalize(List<Ingredient> ingredients)
    {
        if (ingredients == null) return;
        for (int i = 0; i < ingredients.Count; i++)
        {
            if (ingredients[i] != null)
            {
                ingredients[i].amount = Mathf.Max(1, ingredients[i].amount);
            }
        }
    }
}
