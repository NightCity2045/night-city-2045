using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats;

/// <summary>
/// Stores base, progression and temporary parts of a value together with the current final result.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class NCTrackedValue
{
    [DataField("base")]
    public int BaseValue;

    [DataField("progression")]
    public int ProgressionValue;

    [DataField("temporary")]
    public int TemporaryValue;

    [DataField("final")]
    public int FinalValue;

    public NCTrackedValue()
    {
    }

    public NCTrackedValue(int baseValue, int progressionValue = 0, int temporaryValue = 0)
    {
        BaseValue = baseValue;
        ProgressionValue = progressionValue;
        TemporaryValue = temporaryValue;
        FinalValue = baseValue + progressionValue + temporaryValue;
    }
}
