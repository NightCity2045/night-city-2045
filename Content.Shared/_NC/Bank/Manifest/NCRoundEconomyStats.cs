using Robust.Shared.Serialization;

namespace Content.Shared._NC.Bank.Manifest;

/// <summary>
/// Serializable round-local economy snapshot displayed in the round-end summary.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class NCRoundEconomyStats
{
    [DataField]
    public List<NCRoundPlayerEconomyEntry> TopEarned = new();

    [DataField]
    public List<NCRoundPlayerEconomyEntry> TopLost = new();

    [DataField]
    public List<NCRoundFactionEconomyEntry> Factions = new();
}

/// <summary>
/// Gross player bank movement accumulated during one round.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class NCRoundPlayerEconomyEntry
{
    [DataField]
    public string OocName = string.Empty;

    [DataField]
    public string CharacterName = string.Empty;

    [DataField]
    public int Earned;

    [DataField]
    public int Lost;
}

/// <summary>
/// Gross faction-account movement accumulated during one round.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class NCRoundFactionEconomyEntry
{
    [DataField]
    public SectorBankAccount Account;

    [DataField]
    public int Earned;

    [DataField]
    public int Lost;
}
