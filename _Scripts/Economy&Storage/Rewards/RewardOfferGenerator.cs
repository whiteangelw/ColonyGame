using System.Collections.Generic;
using UnityEngine;

public class RewardOfferGenerator : MonoBehaviour
{
    public static RewardOfferGenerator Instance { get; private set; }

    [SerializeField] private List<RewardCardDefinition> cardCatalog =
        new List<RewardCardDefinition>();

    private readonly List<RewardCardDefinition> candidateBuffer =
        new List<RewardCardDefinition>();

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

    public List<RewardCardDefinition> GenerateOffer(int amount = 3)
    {
        List<RewardCardDefinition> offer =
            new List<RewardCardDefinition>();

        candidateBuffer.Clear();

        for (int i = 0; i < cardCatalog.Count; i++)
        {
            RewardCardDefinition card = cardCatalog[i];

            if (card != null
                && RewardService.Instance != null
                && RewardService.Instance.CanOffer(card))
            {
                candidateBuffer.Add(card);
            }
        }

        int draws = Mathf.Min(amount, candidateBuffer.Count);

        for (int i = 0; i < draws; i++)
        {
            RewardCardDefinition selected = DrawWeightedCard();

            if (selected == null)
            {
                break;
            }

            offer.Add(selected);
            candidateBuffer.Remove(selected);
        }

        return offer;
    }

    private RewardCardDefinition DrawWeightedCard()
    {
        float totalWeight = 0f;

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            totalWeight += candidateBuffer[i].GetWeightedChance();
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float roll = Random.value * totalWeight;

        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            roll -= candidateBuffer[i].GetWeightedChance();

            if (roll <= 0f)
            {
                return candidateBuffer[i];
            }
        }

        return candidateBuffer[candidateBuffer.Count - 1];
    }
}
