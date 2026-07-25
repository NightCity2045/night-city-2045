using Content.Server._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

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
    [Dependency] private readonly IGameTiming _timing = default!;

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
        if (HasComp<ActiveMetaDaemonProcessComponent>(hostUid))
            return;

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
            _api.SetIntruder(hostUid, null);
            _api.SetEventSource(hostUid, null);

            if (result.Continuation != null)
            {
                StoreContinuation(hostUid, resolvedServer, daemon.Shard.Value, result);
                return;
            }

            FinishDaemon(hostUid, resolvedServer, daemon.Shard.Value);
        }
        catch (Exception exception)
        {
            Logger.ErrorS("meta", $"Defensive daemon on {ToPrettyString(hostUid)} failed safely: {exception}");
            FinishDaemon(hostUid, resolvedServer, daemon.Shard.Value);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<ActiveMetaDaemonProcessComponent>();
        while (query.MoveNext(out var hostUid, out var active))
        {
            if (currentTime < active.ResumeAtTime)
                continue;

            if (Deleted(active.Server) || Deleted(active.Shard))
            {
                FinishDaemon(hostUid, active.Server, active.Shard);
                continue;
            }

            try
            {
                var result = _vm.Resume(active.Continuation);
                if (result.Continuation != null)
                {
                    StoreContinuation(hostUid, active.Server, active.Shard, result, active);
                    continue;
                }
            }
            catch (Exception exception)
            {
                Logger.ErrorS("meta", $"Defensive daemon on {ToPrettyString(hostUid)} failed safely: {exception}");
            }

            FinishDaemon(hostUid, active.Server, active.Shard);
        }
    }

    private void StoreContinuation(
        EntityUid hostUid,
        EntityUid serverUid,
        EntityUid shardUid,
        MetaVmRunResult result,
        ActiveMetaDaemonProcessComponent? active = null)
    {
        if (result.Continuation == null)
            return;

        active ??= EnsureComp<ActiveMetaDaemonProcessComponent>(hostUid);
        active.Continuation = result.Continuation;
        active.Server = serverUid;
        active.Shard = shardUid;
        active.ResumeAtTime = result.Result.SuspensionReason == MetaSuspensionReason.Yield
            ? _timing.CurTime.TotalSeconds + result.Continuation.ResumeAtTime / 1000.0
            : _timing.CurTime.TotalSeconds;
    }

    private void FinishDaemon(EntityUid hostUid, EntityUid serverUid, EntityUid shardUid)
    {
        _api.SetIntruder(hostUid, null);
        _api.SetEventSource(hostUid, null);
        RemComp<ActiveMetaDaemonProcessComponent>(hostUid);

        if (TryComp<NetServerComponent>(serverUid, out var server))
        {
            server.ActiveMetaPrograms = Math.Max(0, server.ActiveMetaPrograms - 1);
            Dirty(serverUid, server);
            RaiseLocalEvent(serverUid, new MetaServerRuntimeChangedEvent());
        }

        if (TryComp<DataShardComponent>(shardUid, out var shard))
        {
            shard.RuntimeState = MetaProgramRuntimeState.Ready;
            Dirty(shardUid, shard);
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
