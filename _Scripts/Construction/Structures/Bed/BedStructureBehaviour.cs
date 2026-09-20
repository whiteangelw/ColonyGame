using UnityEngine;

[DisallowMultipleComponent]
public sealed class BedStructureBehaviour : MonoBehaviour,
    IConfiguredStructureBehaviour
{
    private ConfiguredStructure structure;
    private DuplicantController reservedBy;
    private DuplicantController occupiedBy;

    public bool UsesSpecializedSaveData => false;
    public Vector2Int GridPosition => structure != null
        ? structure.GridPosition
        : Vector2Int.zero;
    public bool IsOperational => structure != null
        && structure.IsInitialized
        && !structure.IsBeingDismantled
        && isActiveAndEnabled;
    public bool IsReserved => reservedBy != null;
    public bool IsOccupied => occupiedBy != null;
    public DuplicantController ReservedBy => reservedBy;
    public DuplicantController OccupiedBy => occupiedBy;

    public void InitializeStructureBehaviour(ConfiguredStructure owner)
    {
        structure = owner;
        reservedBy = null;
        occupiedBy = null;
        BedRegistry.Register(this);
    }

    public void ShutdownStructureBehaviour()
    {
        BedRegistry.Unregister(this);
        reservedBy = null;
        occupiedBy = null;
        structure = null;
    }

    private void OnEnable()
    {
        if (structure != null && structure.IsInitialized)
        {
            BedRegistry.Register(this);
        }
    }

    private void OnDisable()
    {
        BedRegistry.Unregister(this);
        reservedBy = null;
        occupiedBy = null;
    }

    public bool IsAvailableFor(DuplicantController duplicant)
    {
        return duplicant != null
            && IsOperational
            && (reservedBy == null || reservedBy == duplicant)
            && (occupiedBy == null || occupiedBy == duplicant);
    }

    public bool TryReserve(DuplicantController duplicant)
    {
        if (!IsAvailableFor(duplicant)) return false;
        reservedBy = duplicant;
        return true;
    }

    public bool TryOccupy(DuplicantController duplicant)
    {
        if (duplicant == null
            || !IsOperational
            || reservedBy != duplicant
            || (occupiedBy != null && occupiedBy != duplicant))
        {
            return false;
        }

        occupiedBy = duplicant;
        return true;
    }

    public bool IsUsableBy(DuplicantController duplicant)
    {
        return duplicant != null
            && IsOperational
            && reservedBy == duplicant
            && (occupiedBy == null || occupiedBy == duplicant);
    }

    public void Release(DuplicantController duplicant)
    {
        if (duplicant == null) return;
        if (occupiedBy == duplicant) occupiedBy = null;
        if (reservedBy == duplicant) reservedBy = null;
    }
}
