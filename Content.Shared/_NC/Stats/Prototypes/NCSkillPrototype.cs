using System.IO;
using Content.Shared._NC.Stats.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats.Prototypes;

/// <summary>
/// Describes one skill for validation, UI grouping and future system lookups.
/// </summary>
[Prototype("ncSkill")]
public sealed partial class NCSkillPrototype : IPrototype, ISerializationHooks
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("nameKey", required: true)]
    public string NameKey { get; private set; } = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey { get; private set; } = string.Empty;

    [DataField("categoryKey", required: true)]
    public string CategoryKey { get; private set; } = string.Empty;

    [DataField("governingStat")]
    public string? GoverningStat { get; private set; }

    [DataField("minValue")]
    public int MinValue { get; private set; } = 0;

    [DataField("maxValue")]
    public int MaxValue { get; private set; } = 10;

    [DataField("costMultiplier")]
    public int CostMultiplier { get; private set; } = 1;

    [DataField("mandatoryForCharacters")]
    public bool MandatoryForCharacters { get; private set; }

    [DataField("defaultBaseValue")]
    public int DefaultBaseValue { get; private set; }

    void ISerializationHooks.AfterDeserialization()
    {
        if (string.IsNullOrWhiteSpace(ID))
            throw new InvalidDataException("ncSkill prototype has an empty id.");

        if (MinValue > MaxValue)
            throw new InvalidDataException($"ncSkill {ID} has minValue greater than maxValue.");

        if (CostMultiplier <= 0)
            throw new InvalidDataException($"ncSkill {ID} has non-positive costMultiplier.");

        if (DefaultBaseValue < MinValue || DefaultBaseValue > MaxValue)
            throw new InvalidDataException($"ncSkill {ID} has defaultBaseValue outside of its allowed range.");

        if (GoverningStat != null && string.IsNullOrWhiteSpace(GoverningStat))
            throw new InvalidDataException($"ncSkill {ID} has an empty governingStat.");
    }
}
