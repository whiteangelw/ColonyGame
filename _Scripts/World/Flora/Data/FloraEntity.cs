using UnityEngine;

[DisallowMultipleComponent]
public class FloraEntity : MonoBehaviour
{
    [SerializeField] protected FloraDefinitionSO definition;
    [SerializeField, Min(0)] protected int availableUnits;

    public string FloraId => definition != null ? definition.floraId : string.Empty;
    public int AvailableUnits => availableUnits;
    public Vector2Int GridPosition => GridManager.Instance != null
        ? GridManager.Instance.WorldToGridPosition(transform.position)
        : Vector2Int.zero;

    protected virtual void OnDisable()
    {
        FloraManager.Instance?.Unregister(this);
    }

    public virtual void Initialize(FloraDefinitionSO floraDefinition, int units = -1)
    {
        definition = floraDefinition;
        availableUnits = units >= 0
            ? units
            : (definition != null ? definition.initialPortions : 1);
    }
}
