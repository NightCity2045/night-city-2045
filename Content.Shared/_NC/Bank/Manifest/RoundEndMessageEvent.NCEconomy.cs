using Content.Shared._NC.Bank.Manifest;

namespace Content.Shared.GameTicking;

public sealed partial class RoundEndMessageEvent
{
    /// <summary>
    /// Gross player and faction bank movement for the completed round.
    /// </summary>
    [DataField]
    public NCRoundEconomyStats NCEconomyStats = new();
}
