using Content.Shared._NC.Netrunning.Components;
using Content.Shared.Popups;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Owns persistent NET defenses after META has spawned them.
///     This keeps defense lifecycle and active demon pulses out of components.
/// </summary>
public sealed class NetDefenseSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaApiSystem _metaApi = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetDefenseComponent, ComponentShutdown>(OnDefenseShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NetDemonComponent, NetDefenseComponent>();
        while (query.MoveNext(out var uid, out var demon, out var defense))
        {
            demon.Accumulator += frameTime;
            if (demon.Accumulator < demon.PulseInterval)
                continue;

            demon.Accumulator = 0f;
            PulseDemon(uid, demon, defense);
        }
    }

    private void PulseDemon(EntityUid uid, NetDemonComponent demon, NetDefenseComponent defense)
    {
        foreach (var target in _lookup.GetEntitiesInRange<NetAvatarComponent>(Transform(uid).Coordinates, demon.Range))
        {
            if (target.Owner == uid)
                continue;

            if (defense.OwnerDeck is { } ownerDeck &&
                target.Comp.Cyberdeck == ownerDeck)
                continue;

            _metaApi.ApplyNeuralDamage(target.Owner, demon.Damage);
            _popup.PopupEntity("BLACK ICE bites through your neural link.", target.Owner, target.Owner, PopupType.LargeCaution);
        }
    }

    private void OnDefenseShutdown(EntityUid uid, NetDefenseComponent component, ComponentShutdown args)
    {
        if (component.Server is not { } serverUid ||
            Deleted(serverUid) ||
            !TryComp<NetServerComponent>(serverUid, out var server))
            return;

        server.SpawnedDefenses.Remove(uid);
        server.UsedLoad = Math.Max(0, server.UsedLoad - component.ReservedLoad);
        Dirty(serverUid, server);
    }
}
