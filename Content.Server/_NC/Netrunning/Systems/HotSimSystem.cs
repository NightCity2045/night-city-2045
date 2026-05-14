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

    private MapId? _netMap;
    private EntityUid? _netGrid;

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

        // 1. Prepare Net Map (Lazy load)
        if (_netMap == null || !_mapManager.MapExists(_netMap.Value))
        {
            _netMap = _mapManager.CreateMap();
            
            // Ensure map has light
            var mapUid = _mapManager.GetMapEntityId(_netMap.Value);
            EnsureComp<MapLightComponent>(mapUid);
            
            if (_mapLoader.TryLoadGrid(_netMap.Value, new ResPath("/Maps/_NC/NET/net_f1.yml"), out var grid))
            {
                var gridUid = grid.Value.Owner;
                _netGrid = gridUid;
                EnsureComp<GravityComponent>(gridUid);
                EnsureComp<GridAtmosphereComponent>(gridUid);
            }
            else
            {
                Log.Error("Failed to load Maps/_NC/NET/net_f1.yml grid!");
                return;
            }
        }

        if (_netGrid == null)
            return;

        // 2. Start Immersion Effect (Close eyes)
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), user);

        // 3. Wait for fade then transfer
        Timer.Spawn(TimeSpan.FromSeconds(1.6f), () => 
        {
            if (Deleted(user) || Deleted(uid)) return;

            // Freeze physical body
            _stun.TryParalyze(user, TimeSpan.FromHours(1), true);

            // Spawn Avatar
            var coords = new EntityCoordinates(_netGrid.Value, 0, 0);
            var avatar = Spawn("NCNetAvatar", coords);
            
            var avatarComp = EnsureComp<NetAvatarComponent>(avatar);
            avatarComp.PhysicalBody = user;
            avatarComp.Cyberdeck = uid;

            // Transfer Mind (Visit)
            _mindSystem.Visit(mindId, avatar, mind);

            // Add Jack Out Action
            _actions.AddAction(avatar, "ActionNetJackOut");

            // 4. Open eyes
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
