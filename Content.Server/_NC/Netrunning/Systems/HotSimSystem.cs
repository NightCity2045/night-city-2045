using Content.Server.Mind;
using Content.Shared._NC.Netrunning;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared.Actions;
using Content.Shared.Stunnable;
using Content.Shared.Gravity;
using Content.Server.Atmos.Components;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.Timing;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class HotSimSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly NetSpatialSystem _netSpatial = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckHotSimMessage>(OnHotSim);
        SubscribeLocalEvent<NetAvatarComponent, JackOutActionEvent>(OnJackOut);
    }

    private void OnHotSim(EntityUid uid, CyberdeckComponent component, CyberdeckHotSimMessage args)
    {
        var user = args.Actor;
        if (!user.Valid)
            return;

        if (!_mindSystem.TryGetMind(user, out var mindId, out var mind))
            return;

        // 1. Find anchor node (Physical server or the Deck itself)
        // For now, if the deck doesn't have a node, we assume it's a "scavenged" entry point
        var anchor = uid;
        var node = EnsureComp<NetNodeComponent>(anchor);

        // 2. Get or Create Grid
        var netGrid = _netSpatial.GetOrCreateNetGrid(anchor, node);
        if (netGrid == EntityUid.Invalid)
        {
            Log.Error($"Failed to create digital space for node {ToPrettyString(anchor)}");
            return;
        }

        // 3. Start Immersion Effect (Close eyes)
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), user);

        // 4. Wait for fade then transfer
        Timer.Spawn(TimeSpan.FromSeconds(1.6f), () => 
        {
            if (Deleted(user) || Deleted(uid)) return;

            // Freeze physical body
            _stun.TryParalyze(user, TimeSpan.FromHours(1), true);

            // Spawn Avatar at grid center
            var coords = new EntityCoordinates(netGrid, 0, 0);
            var avatar = Spawn("NCNetAvatar", coords);
            
            var avatarComp = EnsureComp<NetAvatarComponent>(avatar);
            avatarComp.PhysicalBody = user;
            avatarComp.Cyberdeck = uid;

            // Transfer Mind (Visit)
            _mindSystem.Visit(mindId, avatar, mind);

            // Add Jack Out Action
            _actions.AddAction(avatar, "ActionNetJackOut");

            // 5. Open eyes
            RaiseNetworkEvent(new NetrunningImmersionEvent(false), avatar);
        });
    }

    private void OnJackOut(EntityUid uid, NetAvatarComponent component, JackOutActionEvent args)
    {
        if (!_mindSystem.TryGetMind(uid, out var mindId, out var mind))
            return;

        if (component.PhysicalBody is not { } body)
            return;

        // 1. Start Fade Effect (Close eyes)
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), uid);

        // 2. Wait for fade then return
        Timer.Spawn(TimeSpan.FromSeconds(1.6f), () => 
        {
            if (Deleted(body)) return;

            // Return to body
            _mindSystem.UnVisit(mindId, mind);

            // Unfreeze body
            RemComp<StunnedComponent>(body);

            // Cleanup
            QueueDel(uid);

            // 3. Open eyes
            RaiseNetworkEvent(new NetrunningImmersionEvent(false), body);
        });
    }
}
