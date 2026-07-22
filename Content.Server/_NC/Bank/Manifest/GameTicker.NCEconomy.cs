using Content.Server._NC.Bank;
using Content.Shared.GameTicking;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private readonly BankSystem _ncBankSystem = default!;

    /// <summary>
    /// Freezes round-local economy totals into the message before it is sent to clients.
    /// </summary>
    private void AttachNCRoundEconomyStats(RoundEndMessageEvent message)
    {
        message.NCEconomyStats = _ncBankSystem.BuildRoundEconomyStats();
    }
}
