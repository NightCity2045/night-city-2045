using System.IO;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats.Prototypes;

/// <summary>
/// Describes one character stat for UI, validation and downstream systems.
/// </summary>
[Prototype("ncStat")]
public sealed partial class NCStatPrototype : IPrototype, ISerializationHooks
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("nameKey", required: true)]
    public string NameKey { get; private set; } = string.Empty;

    [DataField("shortNameKey", required: true)]
    public string ShortNameKey { get; private set; } = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey { get; private set; } = string.Empty;

    [DataField("minValue")]
    public int MinValue { get; private set; } = 1;

    [DataField("maxValue")]
    public int MaxValue { get; private set; } = 8;

    [DataField("movementSpeedModifiers")]
    public Dictionary<int, float> MovementSpeedModifiers { get; private set; } = new();

    void ISerializationHooks.AfterDeserialization()
    {
        if (string.IsNullOrWhiteSpace(ID))
            throw new InvalidDataException("ncStat prototype has an empty id.");

        if (MinValue > MaxValue)
            throw new InvalidDataException($"ncStat {ID} has minValue greater than maxValue.");

        foreach (var (value, modifier) in MovementSpeedModifiers)
        {
            if (value < MinValue || value > MaxValue)
                throw new InvalidDataException($"ncStat {ID} has movement speed modifier for value {value} outside of its allowed range.");

            if (modifier <= 0f)
                throw new InvalidDataException($"ncStat {ID} has non-positive movement speed modifier for value {value}.");
        }
    }
}
