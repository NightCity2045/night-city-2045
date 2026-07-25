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
public sealed class MetaExecCommand : IConsoleCommand
{
    public string Command => "meta.exec";
    public string Description => "Execute compiled META bytecode from DataShard on Cyberdeck.";
    public string Help => "meta.exec <deckNetEntityUid> <shardNetEntityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!NetEntity.TryParse(args[0], out var deckNet) || !entMan.TryGetEntity(deckNet, out var deckUid))
        {
            shell.WriteError("Invalid deck uid.");
            return;
        }
        if (deckUid == null)
        {
            shell.WriteError("Deck uid resolved to null.");
            return;
        }
        var deckEntity = deckUid.Value;

        if (!NetEntity.TryParse(args[1], out var shardNet) || !entMan.TryGetEntity(shardNet, out var shardUid))
        {
            shell.WriteError("Invalid shard uid.");
            return;
        }
        if (shardUid == null)
        {
            shell.WriteError("Shard uid resolved to null.");
            return;
        }
        var shardEntity = shardUid.Value;

        if (!entMan.TryGetComponent<CyberdeckComponent>(deckEntity, out var deck))
        {
            shell.WriteError("First entity is not Cyberdeck.");
            return;
        }

        if (!entMan.TryGetComponent<DataShardComponent>(shardEntity, out var shard))
        {
            shell.WriteError("Second entity is not DataShard.");
            return;
        }

        var systems = IoCManager.Resolve<IEntitySystemManager>();
        var meta = systems.GetEntitySystem<MetaProgramSystem>();
        if (shard.Bytecode == null && !meta.TryCompile(shardEntity, shard, deckEntity, out var compileError))
        {
            shell.WriteError($"Compile failed: {compileError}");
            return;
        }

        var result = meta.Execute(deckEntity, deck, shardEntity, shard);
        if (result.FatalError != null)
        {
            shell.WriteError($"Execution failed: {result.FatalError}");
            return;
        }

        shell.WriteLine($"Execution success. Gas spent: {result.GasSpent}, reserved RAM: {result.ReservedRam}.");
    }
}
