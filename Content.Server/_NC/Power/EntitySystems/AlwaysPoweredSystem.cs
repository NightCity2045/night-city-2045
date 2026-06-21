using Content.Server._NC.Power.Components;
using Content.Server.Power.Components;
using Content.Shared._NC.Power.Components;

namespace Content.Server._NC.Power.EntitySystems;

/// <summary>
/// Applies a map-level override that makes APC-powered devices behave as if they do not require external power.
/// </summary>
public sealed class AlwaysPoweredSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Re-evaluate receivers when they first appear.
        SubscribeLocalEvent<ApcPowerReceiverComponent, ComponentStartup>(OnReceiverStartup);

        // Re-evaluate receivers when they move between containers / grids / maps.
        SubscribeLocalEvent<ApcPowerReceiverComponent, EntParentChangedMessage>(OnReceiverParentChanged);

        // Re-apply the override for pre-existing entities when the map component itself starts up.
        SubscribeLocalEvent<AlwaysPoweredComponent, ComponentStartup>(OnAlwaysPoweredStartup);
    }

    private void OnReceiverStartup(EntityUid uid, ApcPowerReceiverComponent component, ComponentStartup args)
    {
        RefreshOverride(uid, component);
    }

    private void OnReceiverParentChanged(EntityUid uid, ApcPowerReceiverComponent component, ref EntParentChangedMessage args)
    {
        RefreshOverride(uid, component);
    }

    private void OnAlwaysPoweredStartup(EntityUid uid, AlwaysPoweredComponent component, ComponentStartup args)
    {
        // This is a one-time reconciliation pass in case map startup order places receivers before the map marker.
        var enumerator = EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
        while (enumerator.MoveNext(out var receiverUid, out var receiver, out var xform))
        {
            if (xform.MapUid != uid)
                continue;

            RefreshOverride(receiverUid, receiver);
        }
    }

    /// <summary>
    /// Enables or removes the power bypass depending on whether the receiver currently belongs to an always-powered map.
    /// </summary>
    private void RefreshOverride(EntityUid uid, ApcPowerReceiverComponent receiver)
    {
        var xform = Transform(uid);
        var isAlwaysPoweredMap = xform.MapUid != null && HasComp<AlwaysPoweredComponent>(xform.MapUid.Value);

        if (isAlwaysPoweredMap)
        {
            ApplyOverride(uid, receiver);
            return;
        }

        RestoreOverride(uid, receiver);
    }

    private void ApplyOverride(EntityUid uid, ApcPowerReceiverComponent receiver)
    {
        // Preserve the authored prototype/runtime value exactly once so it can be restored later.
        if (!TryComp<AlwaysPoweredOverrideComponent>(uid, out var overrideComp))
        {
            overrideComp = AddComp<AlwaysPoweredOverrideComponent>(uid);
            overrideComp.OriginalNeedsPower = receiver.NeedsPower;
        }

        if (!receiver.NeedsPower)
            return;

        // Setting NeedsPower=false makes the vanilla power pipeline treat the device as always powered.
        receiver.NeedsPower = false;
        Dirty(uid, receiver);
    }

    private void RestoreOverride(EntityUid uid, ApcPowerReceiverComponent receiver)
    {
        if (!TryComp<AlwaysPoweredOverrideComponent>(uid, out var overrideComp))
            return;

        if (receiver.NeedsPower != overrideComp.OriginalNeedsPower)
        {
            // Restore the original requirement and let Pow3r recalculate the authoritative Powered state.
            receiver.NeedsPower = overrideComp.OriginalNeedsPower;
            Dirty(uid, receiver);
        }

        RemCompDeferred<AlwaysPoweredOverrideComponent>(uid);
    }
}
