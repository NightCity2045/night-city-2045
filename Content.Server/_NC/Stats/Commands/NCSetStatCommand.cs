using Content.Server.Administration;
using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Prototypes;
using Content.Shared._NC.Stats.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Stats.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class NCSetStatCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public string Command => "ncsetstat";
    public string Description => "Sets a Night City base stat on an entity and refreshes dependent systems.";
    public string Help => "Usage: ncsetstat <entityUid> <statId> <baseValue>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netUid) || !_entityManager.TryGetEntity(netUid, out var uid))
        {
            shell.WriteError("Invalid entity UID.");
            return;
        }

        if (!_prototype.TryIndex<NCStatPrototype>(args[1], out var statPrototype))
        {
            shell.WriteError($"Unknown NC stat prototype: {args[1]}");
            return;
        }

        if (!int.TryParse(args[2], out var baseValue))
        {
            shell.WriteError("Base value must be an integer.");
            return;
        }

        var statsSystem = _entityManager.System<SharedNCStatsSystem>();
        if (!statsSystem.SetStatBaseValue(uid.Value, statPrototype.ID, baseValue))
        {
            shell.WriteError($"Entity {uid.Value} does not have NC stats entry {statPrototype.ID}.");
            return;
        }

        // Read back through the stat system so the command reports the clamped final value actually used at runtime.
        if (_entityManager.TryGetComponent(uid, out NCStatsComponent? stats) &&
            statsSystem.TryGetStatValue(stats, statPrototype.ID, out var finalValue))
        {
            shell.WriteLine($"Set {statPrototype.ID} base to {baseValue}; final is {finalValue}.");
        }
    }
}
