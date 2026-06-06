using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._NC.Power.Components;
using Content.Shared._NC.Power.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NC.Power.EntitySystems;

public sealed class LogicPowerSystem : SharedLogicPowerSystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WireSpoolComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LogicPowerReceiverComponent, InteractUsingEvent>(OnInteractUsingReceiver);

        SubscribeLocalEvent<LogicPowerProviderComponent, ComponentShutdown>(OnProviderShutdown);
        SubscribeLocalEvent<LogicPowerReceiverComponent, ComponentShutdown>(OnReceiverShutdown);
    }

    private void OnInteractUsingReceiver(EntityUid uid, LogicPowerReceiverComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Logic for cutting wires
        // Check for wirecutters
        if (!_toolSystem.HasQuality(args.Used, SharedToolSystem.CutQuality))
            return;

        // Also check if player is wearing AR glasses
        if (!HasARVisor(args.User))
            return;

        if (component.Provider == null)
            return;

        Unlink(uid, component);
        _popup.PopupEntity(Loc.GetString("wire-spool-cut"), uid, args.User);
        args.Handled = true;
    }

    private void OnAfterInteract(EntityUid uid, WireSpoolComponent component, AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null)
            return;

        var target = args.Target.Value;

        // Stage 1: Linking to APC
        if (HasComp<ApcComponent>(target))
        {
            component.ActiveProvider = target;
            _popup.PopupEntity(Loc.GetString("wire-spool-linked-apc"), uid, args.User);
            Dirty(uid, component);
            args.Handled = true;
            return;
        }

        // Stage 2: Linking to Consumer
        if (component.ActiveProvider != null && TryComp<LogicPowerReceiverComponent>(target, out var receiverComp))
        {
            var providerUid = component.ActiveProvider.Value;

            if (!HasComp<LogicPowerProviderComponent>(providerUid) && !HasComp<ApcComponent>(providerUid))
            {
                component.ActiveProvider = null;
                Dirty(uid, component);
                return;
            }

            // Distance check
            var distance = (Transform(target).MapPosition.Position - Transform(providerUid).MapPosition.Position).Length();
            if (distance > component.MaxDistance)
            {
                _popup.PopupEntity(Loc.GetString("wire-spool-too-far"), uid, args.User);
                args.Handled = true;
                return;
            }

            // Check if already linked
            if (receiverComp.Provider == providerUid)
            {
                _popup.PopupEntity(Loc.GetString("wire-spool-already-linked"), uid, args.User);
                args.Handled = true;
                return;
            }

            // Consume wire
            if (TryComp<StackComponent>(uid, out var stack) && stack.Count < 1)
            {
                _popup.PopupEntity(Loc.GetString("wire-spool-no-wire"), uid, args.User);
                args.Handled = true;
                return;
            }
            _stack.Use(uid, 1, stack);

            // Establish link
            Link(providerUid, target);

            _popup.PopupEntity(Loc.GetString("wire-spool-success"), uid, args.User);
            args.Handled = true;
            return;
        }
    }

    private bool HasARVisor(EntityUid user)
    {
        if (_inventory.TryGetSlotEntity(user, "eyes", out var eyesItem))
        {
            if (HasComp<ARPowerVisorComponent>(eyesItem))
                return true;
        }
        return HasComp<ARPowerVisorComponent>(user);
    }

    private void OnProviderShutdown(EntityUid uid, LogicPowerProviderComponent component, ComponentShutdown args)
    {
        var receivers = component.Receivers.ToArray();
        foreach (var receiver in receivers)
        {
            Unlink(receiver);
        }
    }

    private void OnReceiverShutdown(EntityUid uid, LogicPowerReceiverComponent component, ComponentShutdown args)
    {
        Unlink(uid, component);
    }

    public void Link(EntityUid providerUid, EntityUid receiverUid)
    {
        Unlink(receiverUid);

        var logicProvider = EnsureComp<LogicPowerProviderComponent>(providerUid);
        var logicReceiver = EnsureComp<LogicPowerReceiverComponent>(receiverUid);

        if (!logicProvider.Receivers.Contains(receiverUid))
            logicProvider.Receivers.Add(receiverUid);

        logicReceiver.Provider = providerUid;

        // CRITICAL: Integrate with vanilla Pow3r solver
        if (TryComp<ApcPowerProviderComponent>(providerUid, out var provider) && 
            TryComp<ApcPowerReceiverComponent>(receiverUid, out var receiver))
        {
            // Sync wattage
            if (TryComp<LogicPowerReceiverComponent>(receiverUid, out var targetReceiver))
            {
                receiver.Load = targetReceiver.PowerLoad;
            }

            provider.AddReceiver(receiver);
            receiver.Provider = provider;
            
            // If the APC has no net (no cables), the solver might ignore it.
            // PowerNetSystem will handle creating the group for the APC's own node.
        }

        Dirty(providerUid, logicProvider);
        Dirty(receiverUid, logicReceiver);
    }

    public void Unlink(EntityUid receiverUid, LogicPowerReceiverComponent? logicReceiver = null)
    {
        if (!Resolve(receiverUid, ref logicReceiver, false) || logicReceiver.Provider == null)
            return;

        var providerUid = logicReceiver.Provider.Value;
        if (TryComp<LogicPowerProviderComponent>(providerUid, out var logicProvider))
        {
            logicProvider.Receivers.Remove(receiverUid);
            Dirty(providerUid, logicProvider);
        }

        // CRITICAL: Clean up from vanilla Pow3r solver
        if (TryComp<ApcPowerProviderComponent>(providerUid, out var provider) && 
            TryComp<ApcPowerReceiverComponent>(receiverUid, out var receiver))
        {
            provider.RemoveReceiver(receiver);
            receiver.Provider = null;
        }

        logicReceiver.Provider = null;
        logicReceiver.Powered = false;
        Dirty(receiverUid, logicReceiver);
    }
}

