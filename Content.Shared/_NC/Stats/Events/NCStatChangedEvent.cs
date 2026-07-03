namespace Content.Shared._NC.Stats.Events;

/// <summary>
/// Raised after an entity stat is recalculated through the NC stats runtime API.
/// </summary>
[ByRefEvent]
public readonly struct NCStatChangedEvent
{
    public readonly string StatId;
    public readonly int OldFinalValue;
    public readonly int NewFinalValue;

    public NCStatChangedEvent(string statId, int oldFinalValue, int newFinalValue)
    {
        StatId = statId;
        OldFinalValue = oldFinalValue;
        NewFinalValue = newFinalValue;
    }
}
