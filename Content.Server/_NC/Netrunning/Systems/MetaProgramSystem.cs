using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaProgramSystem : EntitySystem
{
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DataShardComponent, GetVerbsEvent<ActivationVerb>>(OnShardVerbs);
        SubscribeLocalEvent<CyberdeckComponent, ComponentInit>(OnDeckInit);
        SubscribeLocalEvent<CyberdeckComponent, UseInHandEvent>(OnDeckUse);
        SubscribeLocalEvent<CyberdeckComponent, InteractUsingEvent>(OnDeckInteractUsing);
        SubscribeLocalEvent<CyberdeckComponent, AfterInteractEvent>(OnDeckAfterInteract);
        SubscribeLocalEvent<CyberdeckComponent, BoundUIOpenedEvent>(OnDeckUiOpened);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckCompileMessage>(OnDeckCompile);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckEjectMessage>(OnDeckEject);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckExecuteMessage>(OnDeckExecute);
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
        if (result.FatalError != null)
            _popup.PopupEntity(FormattedMessage.EscapeText(result.FatalError), uid, user, PopupType.MediumCaution);
        else if (result.Yielded)
            _popup.PopupEntity("META: script running...", uid, user);
        else
            _popup.PopupEntity("META script executed.", uid, user);
    }

    private void OnDeckInit(EntityUid uid, CyberdeckComponent component, ComponentInit args)
    {
        _containers.EnsureContainer<Container>(uid, CyberdeckComponent.ShardContainerId);
    }

    private void OnDeckUiOpened(EntityUid uid, CyberdeckComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component, args.Actor);
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

        if (!_compiler.TryCompile(args.Code, MetaProgramKind.Standard, out var bytecode, out var error) || bytecode == null)
        {
            var errText = error ?? "Unknown";
            _popup.PopupEntity($"META compile error: {errText}", uid, user, PopupType.MediumCaution);
            return;
        }

        var programName = string.IsNullOrWhiteSpace(args.Name) ? "Unknown MetaShard" : args.Name;

        if (args.TargetShard != null)
        {
            var shardUid = GetEntity(args.TargetShard.Value);
            if (!TryComp<DataShardComponent>(shardUid, out var shard)) return;
            shard.SourceCode = args.Code;
            shard.Bytecode = bytecode;
            shard.RequiredRam = bytecode.RequiredRam;
            Dirty(shardUid, shard);
            
            _metaData.SetEntityName(shardUid, programName);

            _popup.PopupEntity($"Shard updated: {programName}", uid, user);
        }
        else
        {
            var shardUid = Spawn("NCDataShard", Transform(user).Coordinates);
            var shard = EnsureComp<DataShardComponent>(shardUid);
            shard.SourceCode = args.Code;
            shard.Bytecode = bytecode;
            shard.RequiredRam = bytecode.RequiredRam;
            Dirty(shardUid, shard);

            _metaData.SetEntityName(shardUid, programName);

            _hands.TryPickupAnyHand(user, shardUid);
            _popup.PopupEntity($"New shard generated: {programName}", uid, user);
        }

        UpdateUi(uid, component, user);
    }

    private void UpdateUi(EntityUid uid, CyberdeckComponent component, EntityUid? user = null)
    {
        var shards = new List<(NetEntity, string, string)>();
        if (_containers.TryGetContainer(uid, CyberdeckComponent.ShardContainerId, out var container))
        {
            foreach (var ent in container.ContainedEntities)
            {
                var source = TryComp<DataShardComponent>(ent, out var s) ? s.SourceCode ?? "" : "";
                shards.Add((GetNetEntity(ent), Name(ent), source));
            }
        }

        var hasAR = user != null && TryGetNetvisorBonus(user.Value, out _);
        var state = new CyberdeckUiState(component.CurrentRam, component.MaxRam, GetNetEntity(component.ActiveTarget), shards, hasAR);
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
            _popup.PopupEntity($"Inserted {Name(args.Used)}", uid, args.User);
            UpdateUi(uid, component, args.User);
            args.Handled = true;
        }
    }

    private void OnDeckAfterInteract(EntityUid uid, CyberdeckComponent component, AfterInteractEvent args)
    {
        if (args.Target == null || args.Handled) return;

        var target = args.Target.Value;
        if (Deleted(target)) return;

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
        _popup.PopupEntity(hasAR ? $"Remote link established: {Name(target)}" : $"Physical link established: {Name(target)}", uid, args.User);
        args.Handled = true;
        UpdateUi(uid, component, args.User);
    }

    private void OnShardVerbs(EntityUid uid, DataShardComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract) return;

        args.Verbs.Add(new ActivationVerb
        {
            Text = "Compile META",
            Act = () =>
            {
                if (!TryCompile(uid, component, args.User, out var error))
                {
                    var errText = error ?? "Unknown";
                    _popup.PopupEntity($"META compile error: {errText}", uid, args.User, PopupType.MediumCaution);
                }
                else
                {
                    _popup.PopupEntity("META compile success.", uid, args.User);
                }
            }
        });
    }

    public bool TryCompile(EntityUid shardUid, DataShardComponent shard, EntityUid user, out string? error)
    {
        error = null;
        var source = shard.SourceCode;
        if (string.IsNullOrWhiteSpace(source))
        {
            error = "No source code on shard.";
            return false;
        }

        if (!_compiler.TryCompile(source, shard.ProgramKind, out var bytecode, out error) || bytecode == null)
            return false;

        shard.Bytecode = bytecode;
        shard.RequiredRam = bytecode.RequiredRam;
        Dirty(shardUid, shard);
        return true;
    }

    public MetaExecutionResult Execute(EntityUid deckUid, CyberdeckComponent deck, EntityUid shardUid, DataShardComponent shard)
    {
        if (shard.Bytecode == null) return new MetaExecutionResult(false, false, "Shard has no bytecode.", 0, 0);

        if (!HasActiveLink(deckUid, deck))
            return new MetaExecutionResult(false, false, "No active link. Connect cyberdeck to target first.", 0, 0);

        var effectiveMaxRam = Math.Max(0, deck.MaxRam - deck.LeakedRam);
        if (deck.CurrentRam < shard.RequiredRam || effectiveMaxRam < shard.RequiredRam)
            return new MetaExecutionResult(false, false, "Not enough available RAM.", 0, 0);

        deck.CurrentRam -= shard.RequiredRam;
        Dirty(deckUid, deck);

        var runResult = _vm.Execute(deckUid, shardUid, shard.Bytecode, deck.GasLimit);

        if (runResult.Continuation != null)
        {
            var curTime = _timing.CurTime.TotalSeconds;
            runResult.Continuation.ResumeAtTime = curTime + (runResult.Continuation.ResumeAtTime / 1000.0);
            var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
            active.SuspendedProcesses.Add(runResult.Continuation);
            return runResult.Result;
        }

        deck.CurrentRam = Math.Min(effectiveMaxRam, deck.CurrentRam + shard.RequiredRam);
        Dirty(deckUid, deck);
        return runResult.Result;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var regenQuery = EntityQueryEnumerator<CyberdeckComponent>();
        while (regenQuery.MoveNext(out var deckUid, out var deck))
        {
            var effectiveMax = Math.Max(0, deck.MaxRam - deck.LeakedRam);
            if (deck.CurrentRam >= effectiveMax) continue;

            deck.RecoveryAccumulator += frameTime * deck.RecoverySpeed;
            if (deck.RecoveryAccumulator >= 1f)
            {
                var recovered = (int)deck.RecoveryAccumulator;
                deck.RecoveryAccumulator -= recovered;
                deck.CurrentRam = Math.Min(effectiveMax, deck.CurrentRam + recovered);
                Dirty(deckUid, deck);
            }
        }

        var query = EntityQueryEnumerator<CyberdeckComponent>();
        while (query.MoveNext(out var deckUid2, out var deck2))
        {
            if (deck2.ActiveTarget == null) continue;
            if (!HasActiveLink(deckUid2, deck2))
            {
                deck2.ActiveTarget = null;
                Dirty(deckUid2, deck2);
            }
        }
    }

    private bool HasActiveLink(EntityUid deckUid, CyberdeckComponent deck)
    {
        if (deck.ActiveTarget == null) return false;
        var target = deck.ActiveTarget.Value;
        if (Deleted(target)) return false;

        if (!TryGetDeckUser(deckUid, out var user)) return false;

        float bonus = 0f;
        var hasAR = TryGetNetvisorBonus(user, out bonus);
        var maxRange = hasAR ? (deck.Range + bonus) : 1.5f;

        return _interaction.InRangeUnobstructed(user, target, maxRange);
    }

    private bool TryGetDeckUser(EntityUid deckUid, out EntityUid user)
    {
        user = EntityUid.Invalid;
        if (!TryComp<TransformComponent>(deckUid, out var xform)) return false;
        if (xform.ParentUid == EntityUid.Invalid) return false;
        user = xform.ParentUid;
        return true;
    }
}
