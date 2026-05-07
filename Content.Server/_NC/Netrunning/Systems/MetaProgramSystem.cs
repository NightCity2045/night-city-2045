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

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
/// High-level system that ties together compilation, execution, and YIELD-scheduling.
/// Handles player interactions: inserting DataShards, compiling, running scripts,
/// and linking the cyberdeck to a target via AfterInteract.
///
/// When a script hits YIELD, this system creates an ActiveMetaProcessComponent on the
/// deck and hands off to MetaSchedulerSystem for tick-based resumption.
/// </summary>
public sealed class MetaProgramSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaCompilerSystem _compiler = default!;
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DataShardComponent, GetVerbsEvent<ActivationVerb>>(OnShardVerbs);
        SubscribeLocalEvent<CyberdeckComponent, InteractUsingEvent>(OnDeckInteractUsing);
        SubscribeLocalEvent<CyberdeckComponent, AfterInteractEvent>(OnDeckAfterInteract);
    }

    private void OnDeckInteractUsing(EntityUid uid, CyberdeckComponent component, InteractUsingEvent args)
    {
        if (!TryComp<DataShardComponent>(args.Used, out var shard))
            return;

        if (args.User == EntityUid.Invalid)
            return;

        if (shard.Bytecode == null)
        {
            if (!TryCompile(args.Used, shard, args.User, out var compileError))
            {
                _popup.PopupEntity($"META compile error: {FormattedMessage.EscapeText(compileError ?? "Unknown")}", uid, args.User, PopupType.MediumCaution);
                args.Handled = true;
                return;
            }
        }

        var result = Execute(uid, component, args.Used, shard);
        if (result.FatalError != null)
            _popup.PopupEntity(FormattedMessage.EscapeText(result.FatalError), uid, args.User, PopupType.MediumCaution);
        else if (result.Yielded)
            _popup.PopupEntity("META: script running...", uid, args.User);
        else
            _popup.PopupEntity("META script executed.", uid, args.User);

        args.Handled = true;
    }

    private void OnDeckAfterInteract(EntityUid uid, CyberdeckComponent component, AfterInteractEvent args)
    {
        if (args.Target == null || args.Handled)
            return;

        var target = args.Target.Value;
        if (Deleted(target))
            return;

        if (!_interaction.InRangeUnobstructed(args.User, target, component.Range))
        {
            _popup.PopupEntity("Link failed: target out of range.", uid, args.User, PopupType.MediumCaution);
            return;
        }

        component.ActiveTarget = target;
        Dirty(uid, component);
        _popup.PopupEntity($"Linked to: {Name(target)}", uid, args.User);
        args.Handled = true;
    }

    private void OnShardVerbs(EntityUid uid, DataShardComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new ActivationVerb
        {
            Text = "Compile META",
            Act = () =>
            {
                if (!TryCompile(uid, component, args.User, out var error))
                    _popup.PopupEntity($"META compile error: {FormattedMessage.EscapeText(error ?? "Unknown")}", uid, args.User, PopupType.MediumCaution);
                else
                    _popup.PopupEntity("META compile success.", uid, args.User);
            }
        });

        args.Verbs.Add(new ActivationVerb
        {
            Text = "Execute META",
            Act = () =>
            {
                var deckUid = FindHeldDeck(args.User);
                if (deckUid == null || !TryComp<CyberdeckComponent>(deckUid.Value, out var deck))
                {
                    _popup.PopupEntity("Hold a cyberdeck to run META.", uid, args.User, PopupType.MediumCaution);
                    return;
                }

                if (component.Bytecode == null)
                {
                    _popup.PopupEntity("DataShard is not compiled.", uid, args.User, PopupType.MediumCaution);
                    return;
                }

                var result = Execute(deckUid.Value, deck, uid, component);
                if (result.FatalError != null)
                    _popup.PopupEntity(FormattedMessage.EscapeText(result.FatalError), deckUid.Value, args.User, PopupType.MediumCaution);
                else if (result.Yielded)
                    _popup.PopupEntity("META: script running...", deckUid.Value, args.User);
                else
                    _popup.PopupEntity("META script executed.", deckUid.Value, args.User);
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

    /// <summary>
    /// Execute a META program on a cyberdeck. If the script hits YIELD, the process
    /// is saved into ActiveMetaProcessComponent for tick-based resumption by MetaSchedulerSystem.
    /// </summary>
    public MetaExecutionResult Execute(EntityUid deckUid, CyberdeckComponent deck, EntityUid shardUid, DataShardComponent shard)
    {
        if (shard.Bytecode == null)
            return new MetaExecutionResult(false, false, "Shard has no bytecode.", 0, 0);

        if (!HasActiveLink(deckUid, deck))
            return new MetaExecutionResult(false, false, "No active link. Click cyberdeck on target first.", 0, 0);

        var effectiveMaxRam = Math.Max(0, deck.MaxRam - deck.LeakedRam);
        if (deck.CurrentRam < shard.RequiredRam || effectiveMaxRam < shard.RequiredRam)
            return new MetaExecutionResult(false, false, "Not enough available RAM.", 0, 0);

        // Reserve RAM upfront for the running program.
        deck.CurrentRam -= shard.RequiredRam;
        Dirty(deckUid, deck);

        // Execute the program. The VM returns a continuation if YIELD is hit.
        var runResult = _vm.Execute(deckUid, shardUid, shard.Bytecode, deck.GasLimit);

        if (runResult.Continuation != null)
        {
            // Program is suspended at a YIELD. Save the continuation state for the scheduler.
            var curTime = _timing.CurTime.TotalSeconds;
            var delayMs = runResult.Continuation.ResumeAtTime; // Currently holds ms from YIELD instruction.
            runResult.Continuation.ResumeAtTime = curTime + (delayMs / 1000.0);

            var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
            active.SuspendedProcesses.Add(runResult.Continuation);

            // RAM stays reserved while the process is suspended.
            return runResult.Result;
        }

        // Program completed immediately (no YIELD). Refund base RAM reservation.
        // Memory leaks have already been applied by the VM.
        deck.CurrentRam = Math.Min(effectiveMaxRam, deck.CurrentRam + shard.RequiredRam);
        Dirty(deckUid, deck);
        return runResult.Result;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Passive RAM regeneration for all cyberdecks.
        var regenQuery = EntityQueryEnumerator<CyberdeckComponent>();
        while (regenQuery.MoveNext(out var deckUid, out var deck))
        {
            var effectiveMax = Math.Max(0, deck.MaxRam - deck.LeakedRam);
            if (deck.CurrentRam >= effectiveMax)
                continue;

            deck.RecoveryAccumulator += frameTime * deck.RecoverySpeed;
            if (deck.RecoveryAccumulator >= 1f)
            {
                var recovered = (int)deck.RecoveryAccumulator;
                deck.RecoveryAccumulator -= recovered;
                deck.CurrentRam = Math.Min(effectiveMax, deck.CurrentRam + recovered);
                Dirty(deckUid, deck);
            }
        }

        // Validate active links — disconnect decks that lost their target.
        var query = EntityQueryEnumerator<CyberdeckComponent>();
        while (query.MoveNext(out var deckUid2, out var deck2))
        {
            if (deck2.ActiveTarget == null)
                continue;

            if (!HasActiveLink(deckUid2, deck2))
            {
                deck2.ActiveTarget = null;
                Dirty(deckUid2, deck2);
            }
        }
    }

    private EntityUid? FindHeldDeck(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CyberdeckComponent>(held))
                return held;
        }

        return null;
    }

    private bool HasActiveLink(EntityUid deckUid, CyberdeckComponent deck)
    {
        if (deck.ActiveTarget == null)
            return false;

        var target = deck.ActiveTarget.Value;
        if (Deleted(target))
            return false;

        if (!TryGetDeckUser(deckUid, out var user))
            return false;

        return _interaction.InRangeUnobstructed(user, target, deck.Range);
    }

    private bool TryGetDeckUser(EntityUid deckUid, out EntityUid user)
    {
        user = EntityUid.Invalid;
        if (!TryComp<TransformComponent>(deckUid, out var xform))
            return false;

        if (xform.ParentUid == EntityUid.Invalid)
            return false;

        user = xform.ParentUid;
        return true;
    }
}
