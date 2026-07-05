using System.IO;
using Content.Shared.Alert;
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

    [DataField("bodyKgPerPoint")]
    public float BodyKgPerPoint { get; private set; } = 8f;

    [DataField("bodyLightThreshold")]
    public float BodyLightThreshold { get; private set; } = 0.60f;

    [DataField("bodyHeavyThreshold")]
    public float BodyHeavyThreshold { get; private set; } = 0.85f;

    [DataField("bodyOverloadedThreshold")]
    public float BodyOverloadedThreshold { get; private set; } = 1.00f;

    [DataField("bodyLightWalkModifier")]
    public float BodyLightWalkModifier { get; private set; } = 0.8f;

    [DataField("bodyLightSprintModifier")]
    public float BodyLightSprintModifier { get; private set; } = 0.8f;

    [DataField("bodyHeavyWalkModifier")]
    public float BodyHeavyWalkModifier { get; private set; } = 0.6f;

    [DataField("bodyHeavySprintModifier")]
    public float BodyHeavySprintModifier { get; private set; } = 0f;

    [DataField("bodyOverloadedWalkModifier")]
    public float BodyOverloadedWalkModifier { get; private set; } = 0.1f;

    [DataField("bodyOverloadedSprintModifier")]
    public float BodyOverloadedSprintModifier { get; private set; } = 0f;

    [DataField("bodyLoadAlert")]
    public ProtoId<AlertPrototype> BodyLoadAlert { get; private set; } = "NCBodyLoad";

    [DataField("bodyItemSizeWeights")]
    public Dictionary<string, float> BodyItemSizeWeights { get; private set; } = new();

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

        if (ID != NCStatIds.Body)
            return;

        if (BodyKgPerPoint <= 0f)
            throw new InvalidDataException($"ncStat {ID} has non-positive BODY kg-per-point value.");

        if (BodyLightThreshold < 0f ||
            BodyHeavyThreshold < BodyLightThreshold ||
            BodyOverloadedThreshold < BodyHeavyThreshold)
        {
            throw new InvalidDataException($"ncStat {ID} has invalid BODY load thresholds.");
        }

        if (BodyLightWalkModifier < 0f ||
            BodyLightSprintModifier < 0f ||
            BodyHeavyWalkModifier < 0f ||
            BodyHeavySprintModifier < 0f ||
            BodyOverloadedWalkModifier < 0f ||
            BodyOverloadedSprintModifier < 0f)
        {
            throw new InvalidDataException($"ncStat {ID} has negative BODY movement modifier.");
        }

        foreach (var (size, weight) in BodyItemSizeWeights)
        {
            if (string.IsNullOrWhiteSpace(size))
                throw new InvalidDataException($"ncStat {ID} has an empty BODY item size weight key.");

            if (weight < 0f)
                throw new InvalidDataException($"ncStat {ID} has negative BODY item size weight for {size}.");
        }
    }
}
