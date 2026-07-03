using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats;

/// <summary>
/// One base stat value addressed by prototype ID.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class NCStatEntry
{
    [DataField("statId", required: true)]
    public string StatId = string.Empty;

    [DataField("value", required: true)]
    public NCTrackedValue Value = new();

    public NCStatEntry()
    {
    }

    public NCStatEntry(string statId, NCTrackedValue value)
    {
        StatId = statId;
        Value = value;
    }
}
