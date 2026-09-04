using System.Collections.Generic;
using UnityEngine;

public class RecipeUnlockService : MonoBehaviour
{
    public static RecipeUnlockService Instance { get; private set; }

    [Tooltip("Mantém o comportamento atual enquanto você prepara as cartas de receita.")]
    [SerializeField] private bool startWithAllRecipesUnlocked = true;

    [SerializeField] private List<TileType> initiallyUnlockedRecipes =
        new List<TileType>();

    private readonly HashSet<TileType> unlockedRecipes =
        new HashSet<TileType>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        for (int i = 0; i < initiallyUnlockedRecipes.Count; i++)
        {
            unlockedRecipes.Add(initiallyUnlockedRecipes[i]);
        }
    }

    public bool IsUnlocked(TileType tileType)
    {
        return startWithAllRecipesUnlocked
            || unlockedRecipes.Contains(tileType);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool Unlock(TileType tileType)
    {
        if (IsUnlocked(tileType))
        {
            return false;
        }

        unlockedRecipes.Add(tileType);
        GameEvents.TriggerRecipeUnlocked(tileType);
        return true;
    }
}
