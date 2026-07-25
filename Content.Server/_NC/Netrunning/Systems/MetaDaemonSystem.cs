using Content.Server._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaServerRuntimeChangedEvent : EntityEventArgs
{
}

public sealed class MetaDefenseResponseRequestedEvent : EntityEventArgs
{
    public readonly EntityUid Actor;
    public readonly EntityUid Shard;

    public MetaDefenseResponseRequestedEvent(EntityUid actor, EntityUid shard)
    {
        Actor = actor;
        Shard = shard;
    }
}

public sealed class MetaDaemonSystem : EntitySystem
{
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly MetaApiSystem _api = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private int _nextTransactionId = 1;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DefensiveDaemonComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DefensiveDaemonComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<DefensiveDaemonComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ActiveMetaDaemonProcessComponent, ComponentShutdown>(OnActiveProcessShutdown);
        SubscribeLocalEvent<CyberdeckComponent, ComponentShutdown>(OnDeckShutdown);
        SubscribeNetworkEvent<NetrunningDefenseResponseEvent>(OnDefenseResponse);
    }

    private void OnStartup(EntityUid uid, DefensiveDaemonComponent component, ComponentStartup args)
    {
        SyncShard(uid, component);
    }

    private void OnContainerModified(EntityUid uid, DefensiveDaemonComponent component, ContainerModifiedMessage args)
    {
        SyncShard(uid, component);
    }

    private void OnActiveProcessShutdown(
        EntityUid uid,
        ActiveMetaDaemonProcessComponent component,
        ComponentShutdown args)
    {
        if (!component.Completed)
        {
            _api.SetUser(component.Intruder, null);
            ReleaseDaemonRuntime(uid, component.Server, component.Shard);
        }
    }

    private void OnDeckShutdown(EntityUid uid, CyberdeckComponent component, ComponentShutdown args)
    {
        CancelIntrusions(uid);
    }

    private void SyncShard(EntityUid uid, DefensiveDaemonComponent component)
    {
        var foundShards = new List<EntityUid>();
        foreach (var slotId in component.Slots)
        {
            if (!_containers.TryGetContainer(uid, slotId, out var container))
                continue;

            foreach (var ent in container.ContainedEntities)
            {
                if (HasComp<DataShardComponent>(ent))
                    foundShards.Add(ent);
            }
        }

        if (component.Shards.SequenceEqual(foundShards))
            return;

        component.Shards.Clear();
        component.Shards.AddRange(foundShards);
    }

    /// <summary>
    ///     Synchronizes a runtime-created host immediately after its private shard is inserted.
    /// </summary>
    public void RefreshHostedPrograms(EntityUid hostUid)
    {
        if (TryComp<DefensiveDaemonComponent>(hostUid, out var daemon))
            SyncShard(hostUid, daemon);
    }

    public void NotifyIntrusion(
        EntityUid protectedNode,
        EntityUid intruder,
        EntityUid? explicitFeedbackTarget = null)
    {
        QueueDefenseChain(protectedNode, intruder, 0, out _, explicitFeedbackTarget);
    }

    public bool TryBeginIntrusion(
        EntityUid protectedNode,
        EntityUid intruder,
        MetaIntrusionOperationKind operation,
        int value,
        out MetaIntrusionWait wait,
        EntityUid? explicitFeedbackTarget = null)
    {
        var transactionId = _nextTransactionId++;
        var feedbackTarget = explicitFeedbackTarget ?? _api.ResolveFeedbackTarget(intruder);
        if (!QueueDefenseChain(
                protectedNode,
                intruder,
                transactionId,
                out var serverUid,
                feedbackTarget))
        {
            wait = default;
            return false;
        }

        var defenseQueue = EnsureComp<MetaDefenseQueueComponent>(serverUid);
        defenseQueue.Transactions[transactionId] = new MetaIntrusionTransaction
        {
            Id = transactionId,
            Intruder = intruder,
            FeedbackTarget = feedbackTarget,
            Target = protectedNode,
            Operation = operation,
            Value = value,
        };
        SendDefenseWindow(serverUid, defenseQueue, transactionId);
        TryCompleteTransaction(serverUid, defenseQueue, transactionId);
        wait = new MetaIntrusionWait(GetNetEntity(serverUid), transactionId);
        return true;
    }

    private bool QueueDefenseChain(
        EntityUid protectedNode,
        EntityUid intruder,
        int transactionId,
        out EntityUid resolvedServer,
        EntityUid? explicitFeedbackTarget = null)
    {
        var serverUid = ResolveServer(protectedNode);
        if (serverUid is not { } server || !TryComp<NetServerComponent>(server, out _))
        {
            resolvedServer = EntityUid.Invalid;
            return false;
        }

        var feedbackTarget = explicitFeedbackTarget ?? _api.ResolveFeedbackTarget(intruder);
        var invocations = new List<MetaDefenseInvocation>();
        var uniqueShards = new HashSet<EntityUid>();
        AddDefenseInvocations(
            protectedNode,
            intruder,
            feedbackTarget,
            transactionId,
            invocations,
            uniqueShards);

        if (invocations.Count == 0)
        {
            resolvedServer = server;
            return false;
        }

        var defenseQueue = EnsureComp<MetaDefenseQueueComponent>(server);
        defenseQueue.Pending.AddRange(invocations);

        TryStartNextDefense(server, defenseQueue);
        resolvedServer = server;
        return true;
    }

    private void AddDefenseInvocations(
        EntityUid hostUid,
        EntityUid intruder,
        EntityUid feedbackTarget,
        int transactionId,
        List<MetaDefenseInvocation> invocations,
        HashSet<EntityUid> uniqueShards)
    {
        if (!TryComp<DefensiveDaemonComponent>(hostUid, out var daemon))
            return;

        foreach (var shardUid in daemon.Shards)
        {
            if (uniqueShards.Add(shardUid))
                invocations.Add(new MetaDefenseInvocation(
                    hostUid,
                    shardUid,
                    intruder,
                    feedbackTarget,
                    transactionId));
        }
    }

    private void TryStartNextDefense(EntityUid serverUid, MetaDefenseQueueComponent defenseQueue)
    {
        if (defenseQueue.ActiveHost != null)
            return;

        while (defenseQueue.Pending.Count > 0)
        {
            var invocation = defenseQueue.Pending[0];
            var status = CanStartDaemon(serverUid, invocation, out var shard, out var server);
            if (status == DaemonStartStatus.Blocked)
                return;

            defenseQueue.Pending.RemoveAt(0);
            if (status == DaemonStartStatus.Invalid)
            {
                TryCompleteTransaction(serverUid, defenseQueue, invocation.TransactionId);
                continue;
            }

            defenseQueue.ActiveHost = invocation.Host;
            defenseQueue.ActiveTransactionId = invocation.TransactionId;
            StartDaemon(serverUid, server!, invocation, invocation.Shard, shard!);
            return;
        }

        // The empty queue component is retained to avoid structural changes while
        // defensive processes are being advanced from the system update.
    }

    private DaemonStartStatus CanStartDaemon(
        EntityUid serverUid,
        MetaDefenseInvocation invocation,
        out DataShardComponent? shard,
        out NetServerComponent? server)
    {
        shard = null;
        var hostUid = invocation.Host;
        var shardUid = invocation.Shard;

        if (Deleted(hostUid) ||
            Deleted(shardUid) ||
            ResolveServer(hostUid) != serverUid ||
            !TryComp<DefensiveDaemonComponent>(hostUid, out var daemon) ||
            !daemon.Shards.Contains(shardUid) ||
            !TryComp<DataShardComponent>(shardUid, out shard) ||
            shard.Bytecode == null ||
            !HasIntrusionHandler(shard.Bytecode))
        {
            server = null;
            return DaemonStartStatus.Invalid;
        }

        if (!TryComp<NetServerComponent>(serverUid, out server))
            return DaemonStartStatus.Invalid;

        if (HasComp<ActiveMetaDaemonProcessComponent>(hostUid) ||
            shard.RuntimeState != MetaProgramRuntimeState.Ready ||
            server.ActiveMetaPrograms >= Math.Max(1, server.MaxConcurrentMetaPrograms))
        {
            return DaemonStartStatus.Blocked;
        }

        return DaemonStartStatus.Ready;
    }

    private void StartDaemon(
        EntityUid serverUid,
        NetServerComponent server,
        MetaDefenseInvocation invocation,
        EntityUid shardUid,
        DataShardComponent shard)
    {
        var hostUid = invocation.Host;

        shard.RuntimeState = MetaProgramRuntimeState.Running;
        server.ActiveMetaPrograms++;
        Dirty(shardUid, shard);
        Dirty(serverUid, server);
        RaiseLocalEvent(serverUid, new MetaServerRuntimeChangedEvent());
        _api.SendDefenseWarning(invocation.FeedbackTarget, hostUid);

        try
        {
            _api.SetIntruder(hostUid, invocation.Intruder);
            var result = _vm.ExecuteEvent(hostUid, shardUid, shard.Bytecode!, "INTRUSION", invocation.Intruder,
                Math.Max(1, server.MetaGasLimit));
            _api.SetIntruder(hostUid, null);
            _api.SetEventSource(hostUid, null);

            if (result.Continuation != null)
            {
                StoreContinuation(
                    hostUid,
                    serverUid,
                    shardUid,
                    result,
                    invocation.Intruder,
                    invocation.FeedbackTarget);
                SendDefenseWindow(serverUid, EnsureComp<MetaDefenseQueueComponent>(serverUid),
                    invocation.TransactionId);
                return;
            }

            FinishDaemon(hostUid, serverUid, shardUid);
        }
        catch (Exception exception)
        {
            Logger.ErrorS("meta", $"Defensive daemon on {ToPrettyString(hostUid)} failed safely: {exception}");
            FinishDaemon(hostUid, serverUid, shardUid);
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
                // Restore the physical operator mapping while daemon SYS calls
                // resolve the intruder deck after a YIELD.
                _api.SetUser(active.Intruder, active.FeedbackTarget);
                var result = _vm.Resume(active.Continuation);
                _api.SetUser(active.Intruder, null);
                if (result.Continuation != null)
                {
                    StoreContinuation(hostUid, active.Server, active.Shard, result, active: active);
                    continue;
                }
            }
            catch (Exception exception)
            {
                _api.SetUser(active.Intruder, null);
                Logger.ErrorS("meta", $"Defensive daemon on {ToPrettyString(hostUid)} failed safely: {exception}");
            }

            FinishDaemon(hostUid, active.Server, active.Shard);
        }

        var queueQuery = EntityQueryEnumerator<MetaDefenseQueueComponent>();
        while (queueQuery.MoveNext(out var serverUid, out var defenseQueue))
        {
            if (defenseQueue.ActiveHost == null)
                TryStartNextDefense(serverUid, defenseQueue);
        }
    }

    private void StoreContinuation(
        EntityUid hostUid,
        EntityUid serverUid,
        EntityUid shardUid,
        MetaVmRunResult result,
        EntityUid intruder = default,
        EntityUid feedbackTarget = default,
        ActiveMetaDaemonProcessComponent? active = null)
    {
        if (result.Continuation == null)
            return;

        active ??= EnsureComp<ActiveMetaDaemonProcessComponent>(hostUid);
        active.Continuation = result.Continuation;
        active.Server = serverUid;
        active.Shard = shardUid;
        if (intruder != default)
            active.Intruder = intruder;
        if (feedbackTarget != default)
            active.FeedbackTarget = feedbackTarget;
        active.ResumeAtTime = result.Result.SuspensionReason == MetaSuspensionReason.Yield
            ? _timing.CurTime.TotalSeconds + result.Continuation.ResumeAtTime / 1000.0
            : _timing.CurTime.TotalSeconds;
    }

    private void FinishDaemon(EntityUid hostUid, EntityUid serverUid, EntityUid shardUid)
    {
        _api.SetIntruder(hostUid, null);
        _api.SetEventSource(hostUid, null);

        if (TryComp<ActiveMetaDaemonProcessComponent>(hostUid, out var active))
        {
            active.Completed = true;
            _api.SetUser(active.Intruder, null);
        }

        RemComp<ActiveMetaDaemonProcessComponent>(hostUid);
        ReleaseDaemonRuntime(hostUid, serverUid, shardUid);
    }

    private void ReleaseDaemonRuntime(EntityUid hostUid, EntityUid serverUid, EntityUid shardUid)
    {
        _api.SetIntruder(hostUid, null);
        _api.SetEventSource(hostUid, null);

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

        if (TryComp<MetaDefenseQueueComponent>(serverUid, out var defenseQueue) &&
            defenseQueue.ActiveHost == hostUid)
        {
            var transactionId = defenseQueue.ActiveTransactionId;
            defenseQueue.ActiveHost = null;
            defenseQueue.ActiveTransactionId = 0;
            TryCompleteTransaction(serverUid, defenseQueue, transactionId);
        }
    }

    private void TryCompleteTransaction(
        EntityUid serverUid,
        MetaDefenseQueueComponent defenseQueue,
        int transactionId)
    {
        if (transactionId == 0 || defenseQueue.ActiveTransactionId == transactionId)
            return;

        if (defenseQueue.Pending.Any(invocation => invocation.TransactionId == transactionId))
            return;

        if (!defenseQueue.Transactions.TryGetValue(transactionId, out var transaction))
            return;

        if (transaction.Completed)
            return;

        transaction.Completed = true;
        if (!transaction.Cancelled)
            transaction.Applied = _api.CompleteIntrusion(transaction);

        if (Deleted(transaction.Intruder))
        {
            defenseQueue.Transactions.Remove(transaction.Id);
            return;
        }

        var feedbackTarget = Deleted(transaction.FeedbackTarget)
            ? _api.ResolveFeedbackTarget(transaction.Intruder)
            : transaction.FeedbackTarget;
        RaiseNetworkEvent(new NetrunningDefenseResolvedEvent(transaction.Id, transaction.Applied), feedbackTarget);
    }

    private void OnDefenseResponse(NetrunningDefenseResponseEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } actor)
            return;

        var deckUid = GetEntity(ev.Deck);
        var serverUid = GetEntity(ev.Server);
        var shardUid = GetEntity(ev.Shard);
        if (!TryComp<MetaDefenseQueueComponent>(serverUid, out var queue) ||
            !queue.Transactions.TryGetValue(ev.TransactionId, out var transaction) ||
            transaction.Completed ||
            transaction.Intruder != deckUid)
        {
            return;
        }

        RaiseLocalEvent(deckUid, new MetaDefenseResponseRequestedEvent(actor, shardUid));
    }

    private void SendDefenseWindow(
        EntityUid serverUid,
        MetaDefenseQueueComponent queue,
        int transactionId)
    {
        if (transactionId == 0 ||
            queue.ActiveTransactionId != transactionId ||
            queue.ActiveHost is not { } hostUid ||
            !queue.Transactions.TryGetValue(transactionId, out var transaction) ||
            !TryComp<ActiveMetaDaemonProcessComponent>(hostUid, out var active) ||
            !TryComp<DataShardComponent>(active.Shard, out var defenseShard) ||
            !TryComp<CyberdeckComponent>(transaction.Intruder, out var deck))
        {
            return;
        }

        var responseMilliseconds = Math.Max(0,
            (int) Math.Ceiling((active.ResumeAtTime - _timing.CurTime.TotalSeconds) * 1000.0));
        var shards = new List<NetrunningResponseShardInfo>();
        foreach (var shardUid in deck.InstalledShards)
        {
            if (!TryComp<DataShardComponent>(shardUid, out var shard) ||
                shard.ProgramKind != MetaProgramKind.Standard ||
                shard.RuntimeState != MetaProgramRuntimeState.Ready)
            {
                continue;
            }

            shards.Add(new NetrunningResponseShardInfo(
                GetNetEntity(shardUid),
                Name(shardUid),
                shard.RequiredRam));
        }

        var consequences = CollectConsequences(defenseShard.Bytecode);
        var feedbackTarget = Deleted(transaction.FeedbackTarget)
            ? _api.ResolveFeedbackTarget(transaction.Intruder)
            : transaction.FeedbackTarget;
        RaiseNetworkEvent(new NetrunningDefenseWindowEvent(
            GetNetEntity(transaction.Intruder),
            GetNetEntity(serverUid),
            transactionId,
            Name(active.Shard),
            responseMilliseconds,
            consequences,
            shards), feedbackTarget);
    }

    private static List<NetrunningDefenseConsequence> CollectConsequences(MetaBytecode? bytecode)
    {
        var consequences = new HashSet<NetrunningDefenseConsequence>();
        if (bytecode != null)
            CollectConsequences(bytecode.Instructions, consequences);

        if (consequences.Count == 0)
            consequences.Add(NetrunningDefenseConsequence.Unknown);

        return consequences.ToList();
    }

    private static void CollectConsequences(
        IEnumerable<MetaInstruction> instructions,
        HashSet<NetrunningDefenseConsequence> consequences)
    {
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case MetaSysInjectInstruction:
                    consequences.Add(NetrunningDefenseConsequence.IceDamage);
                    break;
                case MetaSysOverrideInstruction:
                    consequences.Add(NetrunningDefenseConsequence.Override);
                    break;
                case MetaSysSimpleInstruction simple when simple.Name == "BURN_NEUROPORT":
                    consequences.Add(NetrunningDefenseConsequence.NeuralBurn);
                    break;
                case MetaSysSimpleInstruction simple when simple.Name == "DISCONNECT":
                    consequences.Add(NetrunningDefenseConsequence.Disconnect);
                    break;
                case MetaOnEventInstruction onEvent:
                    CollectConsequences(onEvent.Body, consequences);
                    break;
                case MetaIfInstruction conditional:
                    CollectConsequences(conditional.ThenBody, consequences);
                    if (conditional.ElseBody != null)
                        CollectConsequences(conditional.ElseBody, consequences);
                    break;
                case MetaWhileInstruction loop:
                    CollectConsequences(loop.Body, consequences);
                    break;
                case MetaForInstruction loop:
                    CollectConsequences(loop.Body, consequences);
                    break;
            }
        }
    }

    public bool TryConsumeIntrusionResult(EntityUid serverUid, int transactionId, out bool applied)
    {
        applied = false;
        if (!TryComp<MetaDefenseQueueComponent>(serverUid, out var defenseQueue) ||
            !defenseQueue.Transactions.TryGetValue(transactionId, out var transaction) ||
            !transaction.Completed)
        {
            return false;
        }

        applied = transaction.Applied;
        defenseQueue.Transactions.Remove(transactionId);
        return true;
    }

    public bool HasIntrusionTransaction(EntityUid serverUid, int transactionId)
    {
        return TryComp<MetaDefenseQueueComponent>(serverUid, out var queue) &&
               queue.Transactions.ContainsKey(transactionId);
    }

    public void CancelIntrusions(EntityUid intruder)
    {
        var query = EntityQueryEnumerator<MetaDefenseQueueComponent>();
        while (query.MoveNext(out var serverUid, out var defenseQueue))
        {
            foreach (var transaction in defenseQueue.Transactions.Values)
            {
                if (transaction.Intruder == intruder && !transaction.Completed)
                    transaction.Cancelled = true;
            }

            // The active invocation is no longer in Pending. Once the link is cut,
            // no later defensive program should spend server load on that intruder.
            defenseQueue.Pending.RemoveAll(invocation => invocation.Intruder == intruder);

            foreach (var transaction in defenseQueue.Transactions.Values)
                TryCompleteTransaction(serverUid, defenseQueue, transaction.Id);
        }
    }

    private EntityUid? ResolveServer(EntityUid uid, TransformComponent? xform = null)
    {
        if (HasComp<NetServerComponent>(uid))
            return uid;

        if (TryComp<DefensiveDaemonComponent>(uid, out var daemon) &&
            daemon.Server is { } daemonServer &&
            !Deleted(daemonServer))
        {
            return daemonServer;
        }

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

    private static bool HasIntrusionHandler(MetaBytecode bytecode)
    {
        return bytecode.Instructions.Any(instruction =>
            instruction is MetaOnEventInstruction { EventName: var eventName } &&
            eventName.Equals("INTRUSION", StringComparison.OrdinalIgnoreCase));
    }

    private enum DaemonStartStatus : byte
    {
        Invalid,
        Blocked,
        Ready,
    }
}
