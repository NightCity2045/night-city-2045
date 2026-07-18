using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._NC.Administration.Whitelist;

/// <summary>
/// Opens the server whitelist administration panel for an authorized in-game administrator.
/// </summary>
[AdminCommand(AdminFlags.Whitelist)]
public sealed class WhitelistPanelCommand : LocalizedCommands
{
    [Dependency] private readonly EuiManager _eui = default!;

    public override string Command => "whitelistpanel";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 0)
        {
            shell.WriteLine(Help);
            return;
        }

        _eui.OpenEui(new WhitelistPanelEui(), player);
    }
}
