using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats;

/// <summary>
/// One skill value. Specialization is kept only for legacy profile data migration.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class NCSkillEntry
{
    [DataField("skillId", required: true)]
    public string SkillId { get; set; } = string.Empty;

    [DataField("specialization")]
    public string? Specialization { get; set; }

    [DataField("value", required: true)]
    public NCTrackedValue Value { get; set; } = new();

    public NCSkillEntry()
    {
    }

    public NCSkillEntry(string skillId, NCTrackedValue value, string? specialization = null)
    {
        SkillId = skillId;
        Value = value;
        Specialization = specialization;
    }
}
