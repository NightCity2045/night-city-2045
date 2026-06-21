using Content.Shared.CombatMode;
using Content.Shared.Hands;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._NC.ActionPopups;

/// <summary>
/// Shows lightweight world popups for visible player actions that should be readable by nearby players.
/// </summary>
public sealed class NCActionPopupSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, DidEquipHandEvent>(OnDidEquipHand);
        SubscribeLocalEvent<ActorComponent, CombatModeToggledEvent>(OnCombatModeToggled);
    }

    private void OnDidEquipHand(Entity<ActorComponent> ent, ref DidEquipHandEvent args)
    {
        // Only the server should broadcast action popups to nearby players.
        if (_net.IsClient)
            return;

        if (!IsAttachedPlayer(ent))
            return;

        // Ignore helper entities used to reserve extra hands for wielding, carrying, and similar mechanics.
        if (HasComp<VirtualItemComponent>(args.Equipped))
            return;

        var othersMessage = Loc.GetString("nc-action-popup-pickup-others",
            ("user", Identity.Entity(ent.Owner, EntityManager)),
            ("item", args.Equipped));

        _popup.PopupEntity(othersMessage, ent.Owner, Filter.PvsExcept(ent.Owner, entityManager: EntityManager), true);
    }

    private void OnCombatModeToggled(Entity<ActorComponent> ent, ref CombatModeToggledEvent args)
    {
        // Only the server should broadcast action popups to nearby players.
        if (_net.IsClient)
            return;

        if (!IsAttachedPlayer(ent))
            return;

        var othersKey = args.CombatMode
            ? "nc-action-popup-combat-enabled-others"
            : "nc-action-popup-combat-disabled-others";

        var othersMessage = Loc.GetString(othersKey, ("user", Identity.Entity(ent.Owner, EntityManager)));

        _popup.PopupEntity(othersMessage, ent.Owner, Filter.PvsExcept(ent.Owner, entityManager: EntityManager), true);
    }

    /// <summary>
    /// Ensures the popup only fires for a currently attached player-controlled entity.
    /// </summary>
    private static bool IsAttachedPlayer(Entity<ActorComponent> ent)
    {
        return ent.Comp.PlayerSession.AttachedEntity == ent.Owner;
    }
}
