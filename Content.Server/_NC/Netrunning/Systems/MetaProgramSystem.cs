using System;
using System.Collections.Generic;
using Robust.Shared.IoC;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared._NC.Netrunning.Prototypes;
using System.Linq;
using Robust.Shared.Player;
using Content.Shared.Damage;
using Content.Shared.Stunnable;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaProgramStateChangedEvent : EntityEventArgs
{
}

public sealed class MetaProgramSystem : EntitySystem
{
    private const float RamRecoveryInterval = 1f;

    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaCompilerSystem _compiler = default!;
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly Content.Shared.Inventory.InventorySystem _inventory = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly MetaDaemonSystem _daemon = default!;

    private float _ramRecoveryTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DataShardComponent, MapInitEvent>(OnShardMapInit);
        SubscribeLocalEvent<DataShardComponent, GetVerbsEvent<ActivationVerb>>(OnShardVerbs);
        SubscribeLocalEvent<CyberdeckComponent, ComponentInit>(OnDeckInit);
        SubscribeLocalEvent<CyberdeckComponent, EntInsertedIntoContainerMessage>(OnDeckContainerModified);
        SubscribeLocalEvent<CyberdeckComponent, EntRemovedFromContainerMessage>(OnDeckContainerModified);
        SubscribeLocalEvent<CyberdeckComponent, UseInHandEvent>(OnDeckUse);
        SubscribeLocalEvent<CyberdeckComponent, InteractUsingEvent>(OnDeckInteractUsing);
        SubscribeLocalEvent<CyberdeckComponent, AfterInteractEvent>(OnDeckAfterInteract);
        SubscribeLocalEvent<CyberdeckComponent, BoundUIOpenedEvent>(OnDeckUiOpened);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckCompileMessage>(OnDeckCompile);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckEjectMessage>(OnDeckEject);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckExecuteMessage>(OnDeckExecute);
        SubscribeLocalEvent<CyberdeckComponent, MetaDefenseResponseRequestedEvent>(OnDefenseResponseRequested);
    }

    private void OnShardMapInit(EntityUid uid, DataShardComponent component, MapInitEvent args)
    {
        if (string.IsNullOrWhiteSpace(component.SourceCode)) return;
        TryCompile(uid, component, EntityUid.Invalid, out _);
    }

    private void OnDeckUse(EntityUid uid, CyberdeckComponent component, UseInHandEvent args)
    {
        _ui.OpenUi(uid, CyberdeckUiKey.Key, args.User);
        args.Handled = true;
    }

    private void OnDeckEject(EntityUid uid, CyberdeckComponent component, CyberdeckEjectMessage args)
    {
        var user = args.Actor;
        if (!user.Valid) return;
        var shardUid = GetEntity(args.Shard);
        if (TryComp<DataShardComponent>(shardUid, out var shard) &&
            GetRuntimeState(shardUid, shard) != MetaProgramRuntimeState.Ready)
        {
            _popup.PopupEntity(Loc.GetString("netrunning-popup-program-busy"), uid, user, PopupType.MediumCaution);
            return;
        }
        if (!_containers.TryGetContainer(uid, CyberdeckComponent.ShardContainerId, out var container)) return;
        if (_containers.Remove(shardUid, container))
        {
            _hands.TryPickupAnyHand(user, shardUid);
            UpdateUi(uid, component, user);
        }
    }

    private void OnDeckExecute(EntityUid uid, CyberdeckComponent component, CyberdeckExecuteMessage args)
    {
        var user = args.Actor;
        if (!user.Valid) return;
        var shardUid = GetEntity(args.Shard);
        if (!TryComp<DataShardComponent>(shardUid, out var shard)) return;
        var result = Execute(uid, component, shardUid, shard);
        if (result.Failure == MetaExecutionFailure.Rejected && result.FatalError != null)
            _popup.PopupEntity(result.FatalError, uid, user, PopupType.MediumCaution);
    }

    private void OnDeckInit(EntityUid uid, CyberdeckComponent component, ComponentInit args)
    {
        _containers.EnsureContainer<Container>(uid, CyberdeckComponent.ShardContainerId);
        SyncInstalledShards(uid, component);
    }

    private void OnDefenseResponseRequested(
        EntityUid uid,
        CyberdeckComponent component,
        MetaDefenseResponseRequestedEvent args)
    {
        if (!IsDeckControlledBy(uid, args.Actor))
            return;

        TryExecuteDefenseResponse(uid, args.Shard);
    }

    private void OnDeckContainerModified(EntityUid uid, CyberdeckComponent component, ContainerModifiedMessage args)
    {
        SyncInstalledShards(uid, component);
        UpdateUi(uid, component);
    }

    private void SyncInstalledShards(EntityUid uid, CyberdeckComponent component)
    {
        component.InstalledShards.Clear();
        if (!_containers.TryGetContainer(uid, CyberdeckComponent.ShardContainerId, out var container))
            return;

        foreach (var shardUid in container.ContainedEntities)
        {
            if (HasComp<DataShardComponent>(shardUid))
                component.InstalledShards.Add(shardUid);
        }
    }

    private void OnDeckUiOpened(EntityUid uid, CyberdeckComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component, args.Actor);
    }

    private void HandleVmResult(EntityUid deckUid, CyberdeckComponent deck, EntityUid shardUid, MetaVmRunResult runResult, EntityUid user)
    {
        if (runResult.Continuation != null)
        {
            if (!UpdateRunningExecution(deckUid, deck, shardUid, runResult.Result, user))
                return;

            if (runResult.Result.SuspensionReason == MetaSuspensionReason.DefenseResponse)
            {
                EnsureComp<ActiveMetaProcessComponent>(deckUid).SuspendedProcesses.Add(runResult.Continuation);
                _popup.PopupEntity(Loc.GetString("netrunning-meta-defense-wait"), deckUid, user,
                    PopupType.MediumCaution);
                return;
            }

            if (runResult.Result.SuspensionReason == MetaSuspensionReason.SchedulerPreemption)
            {
                runResult.Continuation.ResumeAtTime = _timing.CurTime.TotalSeconds;
                EnsureComp<ActiveMetaProcessComponent>(deckUid).SuspendedProcesses.Add(runResult.Continuation);
                return;
            }

            var delay = (float)runResult.Continuation.ResumeAtTime; 
            var doAfterArgs = new DoAfterArgs(EntityManager, user, delay / 1000f, new AwaitedDoAfterEvent(), deckUid, target: user)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
            };
            if (_doAfter.TryStartDoAfter(doAfterArgs, out var id))
            {
                runResult.Continuation.DoAfterIndex = id.Value.Index;
                var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
                active.SuspendedProcesses.Add(runResult.Continuation);
                _popup.PopupEntity(Loc.GetString("netrunning-meta-processing"), deckUid, user);
            }
            else
            {
                // Fallback to time-based yield
                var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
                var curTime = _timing.CurTime.TotalSeconds;
                runResult.Continuation.ResumeAtTime = curTime + (runResult.Continuation.ResumeAtTime / 1000.0);
                active.SuspendedProcesses.Add(runResult.Continuation);
            }
        }
        else
        {
            FinishExecution(deckUid, deck, shardUid, runResult.Result, user);
        }
    }

    public void FinishExecution(
        EntityUid deckUid,
        CyberdeckComponent deck,
        EntityUid shardUid,
        MetaExecutionResult result,
        EntityUid user,
        bool applyHeat = true)
    {
        if (applyHeat)
            AddExecutionHeat(deck, result);

        if (result.Failure == MetaExecutionFailure.None && IsOverheated(deck))
        {
            result = result with
            {
                Completed = false,
                Yielded = false,
                Failure = MetaExecutionFailure.Overheated,
                SuspensionReason = MetaSuspensionReason.None
            };
        }

        ReleaseReservedRam(deckUid, deck, result.ReservedRam);
        CompleteExecution(deckUid, deck, shardUid);

        deck.LastGasSpent = result.GasSpent;
        deck.LastExecutionRunning = false;
        deck.LastExecutionFailure = result.Failure;
        Dirty(deckUid, deck);

        if (result.Failure == MetaExecutionFailure.GasExhausted)
        {
            ApplyGasFailure(deck, user);
            var message = Loc.GetString("netrunning-meta-gas-fatal",
                ("spent", result.GasSpent),
                ("limit", deck.GasLimit));
            _popup.PopupEntity(message, deckUid, user, PopupType.LargeCaution);
            SendExecutionLog(deckUid, message);
        }
        else if (result.Failure == MetaExecutionFailure.RuntimeError)
        {
            var message = Loc.GetString("netrunning-meta-runtime-fatal",
                ("spent", result.GasSpent),
                ("limit", deck.GasLimit));
            _popup.PopupEntity(message, deckUid, user, PopupType.MediumCaution);
            SendExecutionLog(deckUid, message);
        }
        else if (result.Failure == MetaExecutionFailure.Overheated)
        {
            ApplyGasFailure(deck, user);
            var message = Loc.GetString("netrunning-meta-overheat-fatal",
                ("heat", MathF.Round(deck.CurrentHeat, 1)),
                ("max", MathF.Round(deck.MaxHeat, 1)));
            _popup.PopupEntity(message, deckUid, user, PopupType.LargeCaution);
            SendExecutionLog(deckUid, message);
        }
        else
        {
            var message = Loc.GetString("netrunning-meta-execution-complete",
                ("spent", result.GasSpent),
                ("limit", deck.GasLimit));
            _popup.PopupEntity(message, deckUid, user);
            SendExecutionLog(deckUid, message);
        }

        UpdateUi(deckUid, deck, user);
    }

    public bool UpdateRunningExecution(
        EntityUid deckUid,
        CyberdeckComponent deck,
        EntityUid shardUid,
        MetaExecutionResult result,
        EntityUid user)
    {
        AddExecutionHeat(deck, result);
        if (IsOverheated(deck))
        {
            var overheatResult = result with
            {
                Completed = false,
                Yielded = false,
                Failure = MetaExecutionFailure.Overheated,
                SuspensionReason = MetaSuspensionReason.None
            };
            FinishExecution(deckUid, deck, shardUid, overheatResult, user, applyHeat: false);
            return false;
        }

        deck.LastGasSpent = result.GasSpent;
        deck.LastExecutionRunning = true;
        deck.LastExecutionFailure = MetaExecutionFailure.None;
        Dirty(deckUid, deck);
        UpdateUi(deckUid, deck, user);
        return true;
    }

    private static void AddExecutionHeat(CyberdeckComponent deck, MetaExecutionResult result)
    {
        var generated = result.OperationsThisSlice * Math.Max(0f, deck.HeatPerOperation) +
                        result.SystemCallsThisSlice * Math.Max(0f, deck.HeatPerSystemCall);
        deck.CurrentHeat = Math.Max(0f, deck.CurrentHeat + generated);
    }

    private static bool IsOverheated(CyberdeckComponent deck)
    {
        return deck.MaxHeat > 0f && deck.CurrentHeat >= deck.MaxHeat;
    }

    private void ApplyGasFailure(CyberdeckComponent deck, EntityUid user)
    {
        var immersed = false;
        var target = user;
        if (TryComp<NetAvatarComponent>(user, out var avatar) &&
            avatar != null &&
            avatar.PhysicalBody is { } body &&
            !Deleted(body))
        {
            immersed = true;
            target = body;
        }

        var multiplier = immersed ? Math.Max(0f, deck.HotSimGasFailureMultiplier) : 1f;

        if (deck.GasFailureDamage != null && multiplier > 0f)
            _damageable.TryChangeDamage(target, deck.GasFailureDamage * multiplier,
                ignoreResistances: false, interruptsDoAfters: true);

        var stunDuration = Math.Max(0f, deck.GasFailureStunDuration * multiplier);
        if (stunDuration > 0f)
            _stun.TryParalyze(target, TimeSpan.FromSeconds(stunDuration), true);
    }

    private void SendExecutionLog(EntityUid deckUid, string message)
    {
        _ui.ServerSendUiMessage(deckUid, CyberdeckUiKey.Key, new CyberdeckLogMessage(message));
    }

    public void ReleaseReservedRam(EntityUid deckUid, CyberdeckComponent deck, int amount)
    {
        var released = Math.Clamp(amount, 0, deck.ReservedRam);
        deck.ReservedRam -= released;
        // Completed work leaves RAM exhausted; the deck recovers it over time.
        Dirty(deckUid, deck);
        UpdateUi(deckUid, deck);
    }

    private bool TryGetNetvisorBonus(EntityUid user, out float bonus)
    {
        bonus = 0f;
        bool found = false;
        if (_inventory.TryGetContainerSlotEnumerator(user, out var enumerator))
        {
            while (enumerator.MoveNext(out var slot))
            {
                if (slot.ContainedEntity is { } contained && TryComp<NetvisorComponent>(contained, out var visor))
                {
                    bonus = Math.Max(bonus, visor.BonusRange);
                    found = true;
                }
            }
        }
        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (TryComp<NetvisorComponent>(item, out var visor))
            {
                bonus = Math.Max(bonus, visor.BonusRange);
                found = true;
            }
        }
        return found;
    }

    private void OnDeckCompile(EntityUid uid, CyberdeckComponent component, CyberdeckCompileMessage args)
    {
        var user = args.Actor;
        if (!user.Valid) return;
        if (string.IsNullOrWhiteSpace(args.Code)) return;
        if (!_compiler.TryCompile(args.Code, args.ProgramKind, out var bytecode, out var error) || bytecode == null)
        {
            _popup.PopupEntity("META compile error: " + (error ?? "Unknown"), uid, user, PopupType.MediumCaution);
            return;
        }
        var programName = string.IsNullOrWhiteSpace(args.Name) ? "Unknown MetaShard" : args.Name;
        if (args.TargetShard != null)
        {
            var shardUid = GetEntity(args.TargetShard.Value);
            if (!TryComp<DataShardComponent>(shardUid, out var shard)) return;
            if (GetRuntimeState(shardUid, shard) != MetaProgramRuntimeState.Ready)
            {
                _popup.PopupEntity(Loc.GetString("netrunning-popup-program-busy"), uid, user, PopupType.MediumCaution);
                return;
            }
            shard.SourceCode = args.Code;
            shard.Bytecode = bytecode;
            shard.RequiredRam = bytecode.RequiredRam;
            shard.ProgramKind = args.ProgramKind;
            Dirty(shardUid, shard);
            _metaData.SetEntityName(shardUid, programName);
            _popup.PopupEntity("Shard updated: " + programName, uid, user);
        }
        else
        {
            var shardUid = Spawn("NCDataShard", Transform(user).Coordinates);
            var shard = EnsureComp<DataShardComponent>(shardUid);
            shard.SourceCode = args.Code;
            shard.Bytecode = bytecode;
            shard.RequiredRam = bytecode.RequiredRam;
            shard.ProgramKind = args.ProgramKind;
            Dirty(shardUid, shard);
            _metaData.SetEntityName(shardUid, programName);
            _hands.TryPickupAnyHand(user, shardUid);
            _popup.PopupEntity("New shard generated: " + programName, uid, user);
        }
        UpdateUi(uid, component, user);
    }

    [Dependency] private readonly IPrototypeManager _proto = default!;

    public void UpdateUi(EntityUid uid, CyberdeckComponent component, EntityUid? user = null)
    {
        var shards = new List<(NetEntity, string, string, MetaProgramKind, int, MetaProgramRuntimeState)>();
        if (_containers.TryGetContainer(uid, CyberdeckComponent.ShardContainerId, out var container))
        {
            foreach (var ent in container.ContainedEntities)
            {
                if (!TryComp<DataShardComponent>(ent, out var shard))
                    continue;

                var runtimeState = GetRuntimeState(ent, shard);
                shards.Add((GetNetEntity(ent), Name(ent), shard.SourceCode ?? "", shard.ProgramKind,
                    shard.RequiredRam, runtimeState));
            }
        }

        var modules = new List<NetModuleInfo>();
        foreach (var proto in _proto.EnumeratePrototypes<NetModulePrototype>())
        {
            modules.Add(new NetModuleInfo(proto.ID, proto.Name, proto.Description, proto.RamCost, proto.Price));
        }

        var anchors = new List<NetAnchorInfo>();
        var serverUsedLoad = 0;
        var serverMaxLoad = 0;
        var hasServerAdminAccess = false;
        if (component.ActiveServer is { } serverUid &&
            !Deleted(serverUid) &&
            TryComp<NetServerComponent>(serverUid, out var server) &&
            server.DigitalGrid != null)
        {
            serverUsedLoad = server.UsedLoad;
            serverMaxLoad = server.MaxLoad;
            hasServerAdminAccess = component.AdminNetworks.Contains(serverUid) ||
                                   component.HackedNetworks.Contains(serverUid);

            var coreGridUid = server.DigitalGrid.Value;
            var xformQuery = GetEntityQuery<TransformComponent>();
            if (xformQuery.TryGetComponent(coreGridUid, out var coreXform))
            {
                var mapId = coreXform.MapID;
                var corePos = coreXform.WorldPosition;

                var query = AllEntityQuery<NetAnchorComponent, TransformComponent>();
                while (query.MoveNext(out var aUid, out var anchor, out var xform))
                {
                    if (xform.MapID == mapId && (xform.WorldPosition - corePos).Length() < 150f)
                    {
                        anchors.Add(new NetAnchorInfo(GetNetEntity(aUid), anchor.Direction, anchor.Connected));
                    }
                }
            }
        }

        var hasAR = user != null && TryGetNetvisorBonus(user.Value, out _);
        var state = new CyberdeckUiState(
            component.CurrentRam,
            component.ReservedRam,
            component.MaxRam,
            component.RecoverySpeed,
            component.GasLimit,
            component.LastGasSpent,
            component.LastExecutionRunning,
            component.LastExecutionFailure,
            component.CurrentHeat,
            component.MaxHeat,
            component.CoolingPerSecond,
            component.TraceLevel,
            component.StoredFiles.Count,
            component.StorageCapacity,
            serverUsedLoad,
            serverMaxLoad,
            GetNetEntity(component.ActiveTarget),
            component.ActiveServer != null ? GetNetEntity(component.ActiveServer.Value) : null,
            hasServerAdminAccess,
            shards,
            hasAR,
            modules,
            anchors);
        _ui.SetUiState(uid, CyberdeckUiKey.Key, state);
    }

    private void OnDeckInteractUsing(EntityUid uid, CyberdeckComponent component, InteractUsingEvent args)
    {
        if (!TryComp<DataShardComponent>(args.Used, out var shard)) return;
        if (!_containers.TryGetContainer(uid, CyberdeckComponent.ShardContainerId, out var container)) return;
        if (container.ContainedEntities.Count >= component.MaxShards)
        {
            _popup.PopupEntity("Deck slots full.", uid, args.User, PopupType.MediumCaution);
            return;
        }
        if (_containers.Insert(args.Used, container))
        {
            _popup.PopupEntity("Inserted " + Name(args.Used), uid, args.User);
            UpdateUi(uid, component, args.User);
            args.Handled = true;
        }
    }

    private void OnDeckAfterInteract(EntityUid uid, CyberdeckComponent component, AfterInteractEvent args)
    {
        if (args.Target is not { } target || args.Handled) return;
        if (Deleted(target)) return;

        // 1. Link to Physical Server
        if (HasComp<NetServerComponent>(target))
        {
            component.ActiveServer = target;
            component.ActiveTarget = target;
            Dirty(uid, component);
            
            _ui.OpenUi(uid, CyberdeckUiKey.Key, args.User);
            UpdateUi(uid, component, args.User);
            
            _popup.PopupEntity("Linked to Local Server Hardware.", target, args.User);
            args.Handled = true;
            return;
        }

        // 2. Standard Device Linking (Hacking/AR)
        float bonus = 0f;
        var hasAR = TryGetNetvisorBonus(args.User, out bonus);
        var maxRange = hasAR ? (component.Range + bonus) : 1.5f;
        if (!_interaction.InRangeUnobstructed(args.User, target, maxRange))
        {
            var msg = hasAR ? "Link failed: target out of range." : "Link failed: approach closer to plug in.";
            _popup.PopupEntity(msg, uid, args.User, PopupType.MediumCaution);
            return;
        }
        component.ActiveTarget = target;
        Dirty(uid, component);
        var linkMsg = hasAR ? "Remote link established: " : "Physical link established: ";
        _popup.PopupEntity(linkMsg + Name(target), uid, args.User);
        args.Handled = true;
        UpdateUi(uid, component, args.User);
    }

    public bool TryCompile(EntityUid shardUid, DataShardComponent shard, EntityUid user, out string? error)
    {
        error = null;
        var source = shard.SourceCode;
        if (string.IsNullOrWhiteSpace(source)) { error = "Empty code"; return false; }
        if (!_compiler.TryCompile(source, shard.ProgramKind, out var bytecode, out error) || bytecode == null) return false;
        shard.Bytecode = bytecode;
        shard.RequiredRam = bytecode.RequiredRam;
        Dirty(shardUid, shard);
        return true;
    }

    public MetaExecutionResult Execute(EntityUid deckUid, CyberdeckComponent deck, EntityUid shardUid, DataShardComponent shard)
    {
        return ExecuteInternal(deckUid, deck, shardUid, shard, false);
    }

    private MetaExecutionResult ExecuteInternal(
        EntityUid deckUid,
        CyberdeckComponent deck,
        EntityUid shardUid,
        DataShardComponent shard,
        bool defenseResponse)
    {
        if (shard.Bytecode == null)
            return RejectedExecution(shardUid, Loc.GetString("netrunning-error-no-bytecode"));
        if (shard.ProgramKind == MetaProgramKind.DaemonDefensive)
            return RejectedExecution(shardUid, Loc.GetString("netrunning-cyberdeck-run-defensive-install"));
        if (deck.ActiveTarget == null)
            return RejectedExecution(shardUid, Loc.GetString("netrunning-error-no-link"));
        if (!TryGetDeckUser(deckUid, out var user))
            return RejectedExecution(shardUid, Loc.GetString("netrunning-error-no-user"));
        var runtimeState = GetRuntimeState(shardUid, shard);
        if (runtimeState == MetaProgramRuntimeState.Running)
            return RejectedExecution(shardUid, Loc.GetString("netrunning-error-program-running"));
        if (deck.CurrentRam < shard.RequiredRam || deck.MaxRam < shard.RequiredRam)
            return RejectedExecution(shardUid, Loc.GetString("netrunning-error-out-of-ram"));

        shard.RuntimeState = MetaProgramRuntimeState.Running;
        Dirty(shardUid, shard);
        deck.CurrentRam -= shard.RequiredRam;
        deck.ReservedRam += shard.RequiredRam;
        Dirty(deckUid, deck);

        var target = deck.ActiveTarget.Value;
        MetaVmRunResult runResult;
        if (!defenseResponse &&
            _daemon.TryBeginIntrusion(
                target,
                deckUid,
                MetaIntrusionOperationKind.Program,
                0,
                out var wait,
                user))
        {
            runResult = _vm.PrepareProtectedExecution(
                deckUid,
                user,
                shardUid,
                shard.Bytecode,
                deck.GasLimit,
                target,
                wait);
        }
        else
        {
            runResult = _vm.Execute(
                deckUid,
                user,
                shardUid,
                shard.Bytecode,
                deck.GasLimit,
                target);
        }

        HandleVmResult(deckUid, deck, shardUid, runResult, user);
        UpdateUi(deckUid, deck, user);
        return runResult.Result;
    }

    public bool IsDeckControlledBy(EntityUid deckUid, EntityUid actor)
    {
        return TryGetDeckUser(deckUid, out var user) && user == actor;
    }

    public bool TryExecuteDefenseResponse(EntityUid deckUid, EntityUid shardUid)
    {
        if (!TryComp<CyberdeckComponent>(deckUid, out var deck) ||
            !deck.InstalledShards.Contains(shardUid) ||
            !TryComp<DataShardComponent>(shardUid, out var shard) ||
            shard.ProgramKind != MetaProgramKind.Standard ||
            shard.RuntimeState != MetaProgramRuntimeState.Ready)
        {
            return false;
        }

        return ExecuteInternal(deckUid, deck, shardUid, shard, true).Failure != MetaExecutionFailure.Rejected;
    }

    private MetaExecutionResult RejectedExecution(EntityUid shardUid, string error)
    {
        return new MetaExecutionResult(false, false, error, MetaExecutionFailure.Rejected,
            0, 0, 0, MetaSuspensionReason.None, 0, GetNetEntity(shardUid));
    }

    public MetaProgramRuntimeState GetRuntimeState(EntityUid shardUid, DataShardComponent shard)
    {
        return shard.RuntimeState;
    }

    public void CompleteExecution(EntityUid deckUid, CyberdeckComponent deck, EntityUid shardUid)
    {
        if (!TryComp<DataShardComponent>(shardUid, out var shard))
            return;

        shard.RuntimeState = MetaProgramRuntimeState.Ready;
        Dirty(shardUid, shard);
        UpdateUi(deckUid, deck);
        RaiseLocalEvent(deckUid, new MetaProgramStateChangedEvent());
    }

    public void CancelExecution(EntityUid deckUid, CyberdeckComponent deck, MetaContinuationState process)
    {
        deck.LastExecutionRunning = false;
        Dirty(deckUid, deck);
        ReleaseReservedRam(deckUid, deck, process.ReservedRam);
        CompleteExecution(deckUid, deck, GetEntity(process.ShardUid));
    }

    private void OnShardVerbs(EntityUid uid, DataShardComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract) return;
        args.Verbs.Add(new ActivationVerb {
            Text = "Compile META",
            Act = () => {
                if (GetRuntimeState(uid, component) != MetaProgramRuntimeState.Ready)
                {
                    _popup.PopupEntity(Loc.GetString("netrunning-popup-program-busy"), uid, args.User, PopupType.MediumCaution);
                    return;
                }
                if (!TryCompile(uid, component, args.User, out var err))
                    _popup.PopupEntity("Error: " + err, uid, args.User, PopupType.MediumCaution);
                else _popup.PopupEntity("Success", uid, args.User);
            }
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _ramRecoveryTimer += frameTime;
        if (_ramRecoveryTimer >= RamRecoveryInterval)
        {
            var elapsedSeconds = (int) (_ramRecoveryTimer / RamRecoveryInterval);
            _ramRecoveryTimer -= elapsedSeconds * RamRecoveryInterval;

            var regenQuery = EntityQueryEnumerator<CyberdeckComponent>();
            while (regenQuery.MoveNext(out var deckUid, out var deck))
            {
                var changed = false;
                var cooledHeat = Math.Max(0f,
                    deck.CurrentHeat - elapsedSeconds * Math.Max(0f, deck.CoolingPerSecond));
                if (Math.Abs(cooledHeat - deck.CurrentHeat) > 0.001f)
                {
                    deck.CurrentHeat = cooledHeat;
                    changed = true;
                }

                // Recovery is applied as a visible one-second hardware pulse.
                var effectiveMax = Math.Max(0, deck.MaxRam - deck.ReservedRam);
                if (deck.CurrentRam >= effectiveMax)
                {
                    deck.RecoveryAccumulator = 0f;
                }
                else
                {
                    deck.RecoveryAccumulator += elapsedSeconds * Math.Max(0f, deck.RecoverySpeed);
                    var recovered = (int)deck.RecoveryAccumulator;
                    if (recovered > 0)
                    {
                        deck.RecoveryAccumulator -= recovered;
                        deck.CurrentRam = Math.Min(effectiveMax, deck.CurrentRam + recovered);
                        changed = true;
                    }
                }

                if (changed)
                {
                    Dirty(deckUid, deck);
                    UpdateUi(deckUid, deck);
                }
            }
        }

        var query = EntityQueryEnumerator<CyberdeckComponent>();
        while (query.MoveNext(out var deckUid2, out var deck2))
        {
            if (deck2.ActiveTarget == null) continue;
            if (!HasActiveLink(deckUid2, deck2)) { deck2.ActiveTarget = null; Dirty(deckUid2, deck2); }
        }
    }

    private bool HasActiveLink(EntityUid deckUid, CyberdeckComponent deck)
    {
        if (deck.ActiveTarget == null) return false;
        var target = deck.ActiveTarget.Value;
        if (Deleted(target) || !TryGetDeckUser(deckUid, out var user)) return false;
        float bonus = 0f;
        var hasAR = TryGetNetvisorBonus(user, out bonus);
        return _interaction.InRangeUnobstructed(user, target, hasAR ? (deck.Range + bonus) : 1.5f);
    }

    private bool TryGetDeckUser(EntityUid deckUid, out EntityUid user)
    {
        user = EntityUid.Invalid;

        // In HotSim the deck is linked to the netrunner via NetAvatarComponent,
        // not necessarily through a direct transform parent chain.
        var avatarQuery = EntityQueryEnumerator<NetAvatarComponent>();
        while (avatarQuery.MoveNext(out var avatarUid, out var avatar))
        {
            if (avatar.Cyberdeck != deckUid)
                continue;

            if (HasComp<ActorComponent>(avatarUid))
            {
                user = avatarUid;
                return true;
            }

            if (avatar.PhysicalBody is { } bodyUid &&
                !Deleted(bodyUid) &&
                HasComp<ActorComponent>(bodyUid))
            {
                user = bodyUid;
                return true;
            }
        }

        if (!TryComp<TransformComponent>(deckUid, out var xform))
            return false;

        var current = xform.ParentUid;
        while (current != EntityUid.Invalid && !Deleted(current))
        {
            if (HasComp<ActorComponent>(current))
            {
                user = current;
                return true;
            }

            if (!TryComp<TransformComponent>(current, out var parentXform))
                break;

            current = parentXform.ParentUid;
        }

        return false;
    }
}
