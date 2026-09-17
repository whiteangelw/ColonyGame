using System;

public enum DuplicantIntentType
{
    None,
    Dig,
    Build,
    Haul,
    Dismantle,
    Harvest,
    SeekFood,
    Eat,
    SeekBed,
    Sleep,
    EmergencyRest,
    Hungry,
    Starving,
    Tired,
    WorkFailed
}

public readonly struct DuplicantIntentSignal
{
    public DuplicantIntentType Type { get; }
    public string Details { get; }

    public DuplicantIntentSignal(
        DuplicantIntentType type,
        string details = "")
    {
        Type = type;
        Details = details ?? string.Empty;
    }
}
