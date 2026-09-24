using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CreatureInspectUI : MonoBehaviour
{
    public static CreatureInspectUI Instance { get; private set; }

    [Header("Painel")]
    [SerializeField] private GameObject panelContainer;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI observationText;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private TextMeshProUGUI knowledgeText;

    [Header("Alimentos disponíveis")]
    [SerializeField] private Transform foodOptionsRoot;
    [SerializeField] private Button foodOptionButtonPrefab;

    [Header("Ações")]
    [SerializeField] private Button cancelOrderButton;
    [SerializeField] private Button closeButton;

    private CreatureController selectedCreature;
    private float nextObservationRefresh;
    private bool knowledgeDirty = true;
    private readonly List<CreatureFoodDiscovery> foodDiscoveries =
        new List<CreatureFoodDiscovery>();
    private readonly StringBuilder knowledgeBuilder = new StringBuilder(256);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);

        EnsureOptionsLayout();
        if (IsSceneTemplate(foodOptionButtonPrefab))
            foodOptionButtonPrefab.gameObject.SetActive(false);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        cancelOrderButton?.onClick.AddListener(CancelOrder);
        if (panelContainer != null) panelContainer.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.OnResourceAmountChanged += HandleResourceAmountChanged;
        GameEvents.OnCreatureKnowledgeChanged += HandleKnowledgeChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnResourceAmountChanged -= HandleResourceAmountChanged;
        GameEvents.OnCreatureKnowledgeChanged -= HandleKnowledgeChanged;
        UnsubscribeFromCreature();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (selectedCreature == null)
        {
            if (panelContainer != null && panelContainer.activeSelf) Close();
            return;
        }

        if (Time.unscaledTime < nextObservationRefresh) return;
        nextObservationRefresh = Time.unscaledTime + 0.25f;
        RefreshTextOnly();
    }

    public void Open(CreatureController creature)
    {
        if (creature == null) return;
        UnsubscribeFromCreature();
        selectedCreature = creature;
        knowledgeDirty = true;
        if (selectedCreature.DirectFeeding != null)
            selectedCreature.DirectFeeding.StateChanged += HandleCreatureChanged;
        if (panelContainer != null) panelContainer.SetActive(true);
        Refresh(true);
    }

    public void Close()
    {
        UnsubscribeFromCreature();
        selectedCreature = null;
        knowledgeDirty = true;
        ClearFoodButtons();
        if (panelContainer != null) panelContainer.SetActive(false);
    }

    private void Offer(ResourceType foodType)
    {
        if (selectedCreature == null || selectedCreature.DirectFeeding == null)
            return;

        selectedCreature.DirectFeeding.RequestFeeding(foodType);
        Refresh(true);
    }

    private void CancelOrder()
    {
        selectedCreature?.DirectFeeding?.CancelRequest();
        Refresh(true);
    }

    private void HandleCreatureChanged() => Refresh(true);

    private void HandleResourceAmountChanged(ResourceType type, int amount)
    {
        if (selectedCreature != null) Refresh(true);
    }

    private void HandleKnowledgeChanged(string creatureId)
    {
        if (selectedCreature == null || selectedCreature.Definition == null) return;
        if (!string.IsNullOrEmpty(creatureId)
            && creatureId != selectedCreature.Definition.creatureId)
        {
            return;
        }

        knowledgeDirty = true;
        RefreshTextOnly();
    }

    private void Refresh(bool rebuildOptions)
    {
        if (selectedCreature == null) return;
        RefreshTextOnly();
        if (rebuildOptions) RebuildFoodButtons();
    }

    private void RefreshTextOnly()
    {
        if (selectedCreature == null) return;
        if (titleText != null)
        {
            CreatureDefinitionSO definition = selectedCreature.Definition;
            titleText.text = definition != null
                && !string.IsNullOrWhiteSpace(definition.displayName)
                    ? definition.displayName
                    : "Criatura";
        }

        if (observationText != null)
            observationText.text = BuildObservation(selectedCreature);

        if (knowledgeText != null && knowledgeDirty)
        {
            knowledgeText.text = BuildKnowledgeText(selectedCreature);
            knowledgeDirty = false;
        }

        CreatureDirectFeedingTarget target = selectedCreature.DirectFeeding;
        if (feedbackText != null)
        {
            feedbackText.text = target != null && target.HasPendingRequest
                ? "Uma oferta foi solicitada. Aguarde a entrega."
                : target != null && target.LastRequestResult != "Nenhum pedido"
                    ? target.LastRequestResult
                    : "Escolha um alimento disponível para oferecer.";
        }

        if (cancelOrderButton != null)
            cancelOrderButton.gameObject.SetActive(
                target != null && target.HasPendingRequest);
    }

    private void RebuildFoodButtons()
    {
        ClearFoodButtons();
        if (selectedCreature == null || foodOptionsRoot == null
            || foodOptionButtonPrefab == null) return;

        CreatureDirectFeedingTarget target = selectedCreature.DirectFeeding;
        if (target != null && target.HasPendingRequest) return;

        StockpileManager stockpile = StockpileManager.Instance;
        ItemSpawner itemSpawner = ItemSpawner.Instance;
        if (stockpile == null || itemSpawner == null) return;

        int createdButtons = 0;
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            int amount = stockpile.GetAvailableAmount(type);
            ItemDataSO item = itemSpawner.GetItemData(type);
            if (amount <= 0 || item == null || !item.isFood) continue;

            Button button = Instantiate(foodOptionButtonPrefab, foodOptionsRoot);
            button.gameObject.SetActive(true);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            string displayName = string.IsNullOrWhiteSpace(item.itemName)
                ? type.ToString()
                : item.itemName;
            if (label != null) label.text = displayName + " (" + amount + ")";
            button.interactable = target != null
                && target.DomesticationCanReceiveOffering;
            ResourceType capturedType = type;
            button.onClick.AddListener(() => Offer(capturedType));
            createdButtons++;
        }

        if (createdButtons == 0 && feedbackText != null)
        {
            feedbackText.text = "Nenhum alimento alcançável está disponível.";
        }
    }

    private static string BuildObservation(CreatureController creature)
    {
        if (creature.State == CreatureState.Eating)
            return "Está concentrada no alimento recebido.";
        CreatureDomestication domestication = creature.Domestication;
        if (domestication == null)
            return "Observa o ambiente ao redor.";

        switch (domestication.Mood)
        {
            case CreatureRelationshipMood.Content:
                return "Move-se de maneira leve e curiosa.";
            case CreatureRelationshipMood.Overfed:
                return "Parece inquieta e evita novos alimentos.";
            case CreatureRelationshipMood.Refusing:
                return "Mantém distância e não demonstra interesse.";
            case CreatureRelationshipMood.RejectedOffering:
                return "Afastou-se da última oferta.";
            case CreatureRelationshipMood.Neglected:
                return "Parece menos receptiva à presença da colônia.";
            case CreatureRelationshipMood.Domesticated:
                return "Reconhece a colônia como parte de seu território.";
            default:
                return "Observa a colônia com cautela.";
        }
    }

    private string BuildKnowledgeText(CreatureController creature)
    {
        CreatureKnowledgeService service = CreatureKnowledgeService.Instance;
        if (service == null || creature.Definition == null)
            return "Nenhuma observação registrada.";

        string creatureId = creature.Definition.creatureId;
        service.FillFoodDiscoveries(creatureId, foodDiscoveries);
        bool observedOverfeeding = service.HasObservedOverfeeding(creatureId);
        bool observedRefusal = service.HasObservedTemporaryRefusal(creatureId);

        if (foodDiscoveries.Count == 0
            && !observedOverfeeding
            && !observedRefusal)
        {
            return "Ainda não há descobertas sobre esta espécie.";
        }

        knowledgeBuilder.Clear();
        knowledgeBuilder.Append("Observações:\n");
        for (int i = 0; i < foodDiscoveries.Count; i++)
        {
            CreatureFoodDiscovery discovery = foodDiscoveries[i];
            knowledgeBuilder.Append("• ");
            knowledgeBuilder.Append(GetFoodDisplayName(discovery.ResourceType));
            knowledgeBuilder.Append(discovery.Result
                == CreatureFoodKnowledgeResult.Accepted
                    ? ": aceitou"
                    : ": recusou");
            knowledgeBuilder.Append('\n');
        }

        if (observedOverfeeding)
            knowledgeBuilder.Append("• Muitas ofertas causam desconforto.\n");
        if (observedRefusal)
            knowledgeBuilder.Append("• Às vezes deixa de aceitar novas ofertas.\n");

        if (knowledgeBuilder.Length > 0
            && knowledgeBuilder[knowledgeBuilder.Length - 1] == '\n')
        {
            knowledgeBuilder.Length--;
        }

        return knowledgeBuilder.ToString();
    }

    private static string GetFoodDisplayName(ResourceType type)
    {
        ItemDataSO item = ItemSpawner.Instance != null
            ? ItemSpawner.Instance.GetItemData(type)
            : null;
        return item != null && !string.IsNullOrWhiteSpace(item.itemName)
            ? item.itemName
            : type.ToString();
    }

    private void UnsubscribeFromCreature()
    {
        if (selectedCreature != null && selectedCreature.DirectFeeding != null)
            selectedCreature.DirectFeeding.StateChanged -= HandleCreatureChanged;
    }

    private void ClearFoodButtons()
    {
        if (foodOptionsRoot == null) return;
        for (int i = foodOptionsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = foodOptionsRoot.GetChild(i);
            if (foodOptionButtonPrefab != null
                && child == foodOptionButtonPrefab.transform) continue;
            Destroy(child.gameObject);
        }
    }

    private void EnsureOptionsLayout()
    {
        if (foodOptionsRoot == null) return;
        VerticalLayoutGroup layout =
            foodOptionsRoot.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = foodOptionsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private bool IsSceneTemplate(Button button)
    {
        return button != null && foodOptionsRoot != null
            && button.transform.parent == foodOptionsRoot;
    }
}
