using Content.Server._NC.Netrunning.Systems;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared.Administration;
using Content.Server.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server._NC.Netrunning.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class MetaCompileCommand : IConsoleCommand
{
    public string Command => "meta.compile";
    public string Description => "Compile META source on DataShard.";
    public string Help => "meta.compile <netEntityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!NetEntity.TryParse(args[0], out var netUid) || !entMan.TryGetEntity(netUid, out var uid))
        {
            shell.WriteError("Invalid entity uid.");
            return;
        }
        if (uid == null)
        {
            shell.WriteError("Entity uid resolved to null.");
            return;
        }
        var shardUid = uid.Value;

        if (!entMan.TryGetComponent<DataShardComponent>(shardUid, out var shard))
        {
            shell.WriteError("Entity is not a DataShard.");
            return;
        }

        var systems = IoCManager.Resolve<IEntitySystemManager>();
        var meta = systems.GetEntitySystem<MetaProgramSystem>();
        if (!meta.TryCompile(shardUid, shard, shardUid, out var error))
        {
            shell.WriteError($"Compile failed: {error}");
            return;
        }

        shell.WriteLine("Compile success.");
    }
}
