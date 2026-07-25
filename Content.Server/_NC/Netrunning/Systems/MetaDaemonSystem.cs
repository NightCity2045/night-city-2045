using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaServerRuntimeChangedEvent : EntityEventArgs
{
}

public sealed class MetaDaemonSystem : EntitySystem
{
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly MetaApiSystem _api = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaProgramSystem _program = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DefensiveDaemonComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DefensiveDaemonComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<DefensiveDaemonComponent, EntRemovedFromContainerMessage>(OnContainerModified);
    }

    private void OnStartup(EntityUid uid, DefensiveDaemonComponent component, ComponentStartup args)
    {
        SyncShard(uid, component);
    }

    private void OnContainerModified(EntityUid uid, DefensiveDaemonComponent component, ContainerModifiedMessage args)
    {
        SyncShard(uid, component);
    }

    private void SyncShard(EntityUid uid, DefensiveDaemonComponent component)
    {
        EntityUid? foundShard = null;
        foreach (var container in _containers.GetAllContainers(uid))
        {
            foreach (var ent in container.ContainedEntities)
            {
                if (HasComp<DataShardComponent>(ent))
                {
                    foundShard = ent;
                    break;
                }
            }

            if (foundShard != null)
                break;
        }

        if (component.Shard == foundShard)
            return;

        component.Shard = foundShard;
    }

    public void NotifyIntrusion(EntityUid protectedNode, EntityUid intruder)
    {
        if (TryComp<DefensiveDaemonComponent>(protectedNode, out var daemon))
            TriggerDaemon(protectedNode, daemon, intruder);

        var serverUid = ResolveServer(protectedNode);
        if (serverUid == null)
            return;

        var query = EntityQueryEnumerator<DefensiveDaemonComponent, TransformComponent>();
        while (query.MoveNext(out var daemonUid, out var serverDaemon, out var xform))
        {
            if (daemonUid == protectedNode)
                continue;

            if (ResolveServer(daemonUid, xform) != serverUid)
                continue;

            TriggerDaemon(daemonUid, serverDaemon, intruder);
        }
    }

    private void TriggerDaemon(EntityUid hostUid, DefensiveDaemonComponent daemon, EntityUid intruder)
    {
        if (daemon.Shard == null || !TryComp<DataShardComponent>(daemon.Shard.Value, out var shard))
            return;

        if (shard.Bytecode == null || shard.ProgramKind != MetaProgramKind.DaemonDefensive)
            return;

        var serverUid = ResolveServer(hostUid);
        if (serverUid is not { } resolvedServer ||
            !TryComp<NetServerComponent>(resolvedServer, out var server) ||
            _program.GetRuntimeState(daemon.Shard.Value, shard) != MetaProgramRuntimeState.Ready ||
            server.ActiveMetaPrograms >= Math.Max(1, server.MaxConcurrentMetaPrograms))
            return;

        shard.RuntimeState = MetaProgramRuntimeState.Running;
        server.ActiveMetaPrograms++;
        Dirty(daemon.Shard.Value, shard);
        Dirty(resolvedServer, server);
        RaiseLocalEvent(resolvedServer, new MetaServerRuntimeChangedEvent());

        try
        {
            _api.SetIntruder(hostUid, intruder);
            var result = _vm.ExecuteEvent(hostUid, daemon.Shard.Value, shard.Bytecode, "INTRUSION", intruder,
                Math.Max(1, server.MetaGasLimit));
        }
        catch (Exception exception)
        {
            Logger.ErrorS("meta", $"Defensive daemon on {ToPrettyString(hostUid)} failed safely: {exception}");
        }
        finally
        {
            _api.SetIntruder(hostUid, null);
            _api.SetEventSource(hostUid, null);
            server.ActiveMetaPrograms = Math.Max(0, server.ActiveMetaPrograms - 1);
            shard.RuntimeState = MetaProgramRuntimeState.Ready;
            Dirty(daemon.Shard.Value, shard);
            Dirty(resolvedServer, server);
            RaiseLocalEvent(resolvedServer, new MetaServerRuntimeChangedEvent());
        }
    }

    private EntityUid? ResolveServer(EntityUid uid, TransformComponent? xform = null)
    {
        if (HasComp<NetServerComponent>(uid))
            return uid;

        if (TryComp<NetDeviceNodeComponent>(uid, out var node) && node.Server is { } nodeServer && !Deleted(nodeServer))
            return nodeServer;

        if (TryComp<NetDefenseComponent>(uid, out var defense) && defense.Server is { } defenseServer && !Deleted(defenseServer))
            return defenseServer;

        if (TryComp<NetModuleComponent>(uid, out var module) && module.Server is { } moduleServer && !Deleted(moduleServer))
            return moduleServer;

        xform ??= Transform(uid);
        if (xform.GridUid is not { } gridUid || Deleted(gridUid))
            return null;

        if (HasComp<NetServerComponent>(gridUid))
            return gridUid;

        if (TryComp<NetModuleComponent>(gridUid, out var gridModule) && gridModule.Server is { } server && !Deleted(server))
            return server;

        return null;
    }
}
