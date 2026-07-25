using Content.Shared._NC.Netrunning.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._NC.Netrunning.Systems;

/// <summary>
///     Predicts selective NET-wall collision for the owner and local administrators.
/// </summary>
public sealed class SharedNetBarrierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetBarrierComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(
        Entity<NetBarrierComponent> ent,
        ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !TryComp<NetAvatarComponent>(args.OtherEntity, out var avatar) ||
            avatar.Cyberdeck is not { } deckUid)
        {
            return;
        }

        if (ent.Comp.AllowOwner && deckUid == ent.Comp.OwnerDeck)
        {
            args.Cancelled = true;
            return;
        }

        if (!ent.Comp.AllowNetworkAdmins ||
            ent.Comp.Server is not { } serverUid ||
            !TryComp<CyberdeckComponent>(deckUid, out var deck))
        {
            return;
        }

        if (deck.AdminNetworks.Contains(serverUid) || deck.HackedNetworks.Contains(serverUid))
            args.Cancelled = true;
    }
}
