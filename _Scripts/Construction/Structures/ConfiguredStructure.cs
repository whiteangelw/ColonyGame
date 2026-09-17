using UnityEngine;

[DisallowMultipleComponent]
public sealed class ConfiguredStructure : MonoBehaviour, IDismantlable, IInteractable
{
    public Vector2Int GridPosition { get; private set; }
    public string DefinitionId { get; private set; }
    public bool IsBeingDismantled { get; private set; }
    public bool UsesSpecializedSaveData
    {
        get
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                if (component is IConfiguredStructureBehaviour behaviour
                    && behaviour.UsesSpecializedSaveData)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public BuildDefinitionSO Definition { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize(Vector2Int position, BuildDefinitionSO definition)
    {
        GridPosition = position;
        Definition = definition;
        DefinitionId = definition != null ? definition.DefinitionId : string.Empty;
        IsBeingDismantled = false;
        IsInitialized = false;

        if (definition == null)
        {
            Debug.LogError("[ConfiguredStructure] Definição ausente.", this);
            return;
        }

        if (StructureManager.Instance == null
            || !StructureManager.Instance.RegisterConfiguredStructure(this, definition))
        {
            return;
        }

        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
        {
            if (component is IConfiguredStructureBehaviour behaviour)
            {
                behaviour.InitializeStructureBehaviour(this);
            }
        }

        IsInitialized = true;
    }

    public void OnInteract()
    {
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
        {
            if (component != this && component is IInteractable interactable)
            {
                interactable.OnInteract();
                return;
            }
        }
    }

    public void ShutdownBehaviours()
    {
        foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
        {
            if (component is IConfiguredStructureBehaviour behaviour)
            {
                behaviour.ShutdownStructureBehaviour();
            }
        }
    }

    public void Dismantle()
    {
        if (IsBeingDismantled) return;
        BuildDefinitionSO definition = Definition
            ?? BuildCatalogService.Instance?.GetById(DefinitionId);
        if (definition != null && !definition.CanBeDismantled) return;
        IsBeingDismantled = true;
        WorldInteractionService.Instance?.DismantleTile(
            GridPosition.x,
            GridPosition.y,
            GridLayer.Structure);
    }
}
