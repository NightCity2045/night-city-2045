using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaDaemonSystem : EntitySystem
{
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly MetaApiSystem _api = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaSchedulerSystem _scheduler = default!;

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

        _api.SetIntruder(hostUid, intruder);
        var result = _vm.ExecuteEvent(hostUid, shard.Bytecode, "INTRUSION", intruder);
        _scheduler.HandleVmResult(hostUid, daemon.Shard.Value, result);
        _api.SetIntruder(hostUid, null);
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
