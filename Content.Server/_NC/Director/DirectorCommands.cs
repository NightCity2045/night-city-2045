using Content.Server.Administration;
using Content.Shared._NC.Director;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Director;

[AdminCommand(AdminFlags.Admin)]
public sealed class StartDirectorEventCommand : IConsoleCommand
{
    public string Command => "startdirectorevent";
    public string Description => "Starts a director event by ID.";
    public string Help => "Usage: startdirectorevent <prototypeId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<GlobalDirectorSystem>();
        var protoManager = IoCManager.Resolve<IPrototypeManager>();

        if (!protoManager.TryIndex<DirectorEventPrototype>(args[0], out var proto))
        {
            shell.WriteError($"Unknown director event prototype: {args[0]}");
            return;
        }

        if (proto.Abstract)
        {
            shell.WriteError($"Director event prototype {args[0]} is abstract and cannot be started directly.");
            return;
        }

        var uid = system.StartEvent(proto);
        shell.WriteLine($"Started event {args[0]} as {uid}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class AdvanceDirectorEventCommand : IConsoleCommand
{
    public string Command => "advancedirectorevent";
    public string Description => "Advances a director event to the next phase.";
    public string Help => "Usage: advancedirectorevent <entityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        if (!EntityUid.TryParse(args[0], out var uid))
        {
            shell.WriteError("Invalid entity UID.");
            return;
        }

        var system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<GlobalDirectorSystem>();
        system.AdvancePhase(uid);
        shell.WriteLine($"Advanced event {uid}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class CancelDirectorEventCommand : IConsoleCommand
{
    public string Command => "canceldirectorevent";
    public string Description => "Cancels an active director event and cleans up owned entities.";
    public string Help => "Usage: canceldirectorevent <entityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        if (!EntityUid.TryParse(args[0], out var uid))
        {
            shell.WriteError("Invalid entity UID.");
            return;
        }

        var system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<GlobalDirectorSystem>();
        if (!system.CancelEvent(uid))
        {
            shell.WriteError($"Entity {uid} is not an active director event.");
            return;
        }

        shell.WriteLine($"Cancelled event {uid}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class DirectorStatusCommand : IConsoleCommand
{
    public string Command => "directorstatus";
    public string Description => "Prints scheduler status, active events, and current eligibility of all director prototypes.";
    public string Help => "Usage: directorstatus";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<GlobalDirectorSystem>();
        foreach (var line in system.GetStatusReport())
        {
            shell.WriteLine(line);
        }
    }
}
