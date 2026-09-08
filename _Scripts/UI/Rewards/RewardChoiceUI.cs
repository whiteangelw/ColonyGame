using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RewardChoiceUI : MonoBehaviour
{
    public static RewardChoiceUI Instance { get; private set; }

    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private RewardCardView[] cardViews;

    private bool isResolvingChoice;

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

        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        GameEvents.OnPrintingPodRemoved += HandlePrintingPodRemoved;
    }

    private void OnDisable()
    {
        GameEvents.OnPrintingPodRemoved -= HandlePrintingPodRemoved;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void OpenCurrentOffer()
    {
        if (PrintingPodScheduler.Instance == null
            || !PrintingPodScheduler.Instance.IsOfferReady
            || RewardOfferGenerator.Instance == null
            || cardViews == null
            || cardViews.Length != 3)
        {
            Debug.LogWarning(
                "[RewardChoiceUI] Oferta ou três Card Views não configurados."
            );
            return;
        }

        List<RewardCardDefinition> offer =
            RewardOfferGenerator.Instance.GenerateOffer(3);

        if (offer.Count != 3)
        {
            if (feedbackText != null)
            {
                feedbackText.text =
                    "Configure pelo menos 3 cartas válidas no catálogo.";
            }

            Debug.LogWarning(
                "[RewardChoiceUI] O catálogo não possui 3 cartas válidas."
            );
            SetVisible(true);
            return;
        }

        isResolvingChoice = false;

        if (feedbackText != null)
        {
            feedbackText.text = "Escolha uma recompensa";
        }

        for (int i = 0; i < cardViews.Length; i++)
        {
            cardViews[i].Bind(offer[i], SelectCard);
        }

        SetVisible(true);
    }

    private void SelectCard(RewardCardDefinition card)
    {
        if (isResolvingChoice || RewardService.Instance == null)
        {
            return;
        }

        isResolvingChoice = true;
        SetCardsInteractable(false);

        if (!RewardService.Instance.Apply(card))
        {
            isResolvingChoice = false;
            SetCardsInteractable(true);

            if (feedbackText != null)
            {
                feedbackText.text = "Não foi possível aplicar esta recompensa.";
            }
            return;
        }

        PrintingPodScheduler.Instance?.NotifyOfferClaimed();
        SetVisible(false);
    }

    private void HandlePrintingPodRemoved(PrintingPod printingPod)
    {
        SetVisible(false);
    }

    private void SetCardsInteractable(bool interactable)
    {
        for (int i = 0; i < cardViews.Length; i++)
        {
            if (cardViews[i] != null)
            {
                cardViews[i].SetInteractable(interactable);
            }
        }
    }

    private void SetVisible(bool visible)
    {
        if (panelCanvasGroup == null)
        {
            return;
        }

        panelCanvasGroup.alpha = visible ? 1f : 0f;
        panelCanvasGroup.interactable = visible;
        panelCanvasGroup.blocksRaycasts = visible;
    }
}
