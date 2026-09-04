using UnityEngine;

public class RewardService : MonoBehaviour
{
    public static RewardService Instance { get; private set; }

    [SerializeField] private DuplicantSpawnService duplicantSpawnService;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool CanOffer(RewardCardDefinition card)
    {
        if (card == null)
        {
            return false;
        }

        switch (card.cardType)
        {
            case RewardCardType.Resource:
                return card.resourceAmount > 0
                    && ItemSpawner.Instance != null;

            case RewardCardType.Duplicant:
                return card.duplicantDefinition != null
                    && GetSpawnService() != null;

            case RewardCardType.Recipe:
                return RecipeUnlockService.Instance != null
                    && !RecipeUnlockService.Instance.IsUnlocked(
                        card.recipeTileType
                    );

            // Bônus será ativado quando houver efeitos concretos.
            case RewardCardType.Bonus:
                return false;

            default:
                return false;
        }
    }

    public bool Apply(RewardCardDefinition card)
    {
        if (!CanOffer(card) || PrintingPod.Instance == null)
        {
            return false;
        }

        bool applied;
        Vector3 feedbackPosition =
            PrintingPod.Instance.transform.position + Vector3.up;

        switch (card.cardType)
        {
            case RewardCardType.Resource:
                if (!PrintingPod.Instance.TryGetRewardDropPosition(
                        out Vector3 resourceDropPosition))
                {
                    return false;
                }

                applied = ItemSpawner.Instance.TrySpawnResource(
                    card.resourceType,
                    resourceDropPosition,
                    card.resourceAmount
                );

                feedbackPosition = resourceDropPosition;
                break;

            case RewardCardType.Duplicant:
                applied = GetSpawnService().SpawnDefinitionNear(
                    card.duplicantDefinition,
                    PrintingPod.Instance.GridPosition
                ) != null;
                break;

            case RewardCardType.Recipe:
                applied = RecipeUnlockService.Instance.Unlock(
                    card.recipeTileType
                );
                break;

            default:
                applied = false;
                break;
        }

        if (applied)
        {
            GameEvents.TriggerFloatingTextRequested(
                card.displayName,
                feedbackPosition,
                card.GetRarityColor()
            );
        }

        return applied;
    }

    private DuplicantSpawnService GetSpawnService()
    {
        if (duplicantSpawnService == null)
        {
            duplicantSpawnService =
                FindFirstObjectByType<DuplicantSpawnService>();
        }

        return duplicantSpawnService;
    }
}
