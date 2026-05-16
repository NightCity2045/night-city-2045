using System.Numerics;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Content.Shared.Popups;

using Content.Server.GameTicking.Events;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Handles the transition from local networks to the Global Deep NET.
///     Includes geo-routing logic based on physical world coordinates.
/// </summary>
public sealed class NetGlobalSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly NetSpatialSystem _netSpatial = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public MapId? OldNetMapId;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetDataGateComponent, ActivateInWorldEvent>(OnGateActivate);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        LoadOldNet();
    }

    private void LoadOldNet()
    {
        if (OldNetMapId != null && _mapManager.MapExists(OldNetMapId.Value))
            return;

        if (_mapLoader.TryLoadMap(new ResPath("/Maps/_NC/NET/old_net.yml"), out var map, out _))
        {
            OldNetMapId = map.Value.Comp.MapId;
            Log.Info($"Global Old Net loaded successfully. MapId: {OldNetMapId}");
        }
        else
        {
            Log.Error("Failed to load Global Old Net map!");
        }
    }

    private void OnGateActivate(EntityUid uid, NetDataGateComponent component, ActivateInWorldEvent args)
    {
        if (!TryComp<NetAvatarComponent>(args.User, out var avatar))
            return;

        if (OldNetMapId == null)
        {
            _popup.PopupEntity("ERROR: Old NET is not initialized.", args.User, args.User);
            return;
        }

        // 1. Determine World Position based on physical body
        var body = avatar.PhysicalBody;
        if (body == null) return;

        var bodyWorldPos = Transform(body.Value).WorldPosition;

        // 2. Perform Transition to the single Global Map
        TeleportToGlobalNet(args.User, OldNetMapId.Value, bodyWorldPos);
    }

    public void TransitionToGlobal(EntityUid avatarUid, NetAvatarComponent avatar)
    {
        if (avatar.PhysicalBody == null || OldNetMapId == null) return;

        var bodyWorldPos = Transform(avatar.PhysicalBody.Value).WorldPosition;

        // Perform Transition
        TeleportToGlobalNet(avatarUid, OldNetMapId.Value, bodyWorldPos);
        
        _popup.PopupEntity("CONNECTION HANDOVER: Entering Global NET...", avatarUid, avatarUid);
    }

    private void TeleportToGlobalNet(EntityUid avatar, MapId targetMap, Vector2 targetWorldPos)
    {
        if (!_mapManager.MapExists(targetMap))
        {
            Log.Error($"Attempted to teleport to non-existent Global NET map: {targetMap}");
            return;
        }

        var mapUid = _mapManager.GetMapEntityId(targetMap);
        
        // In the 1:1 city cast model, we teleport exactly to the coordinates where the player is in reality
        _transform.SetParent(avatar, mapUid);
        _transform.SetWorldPosition(avatar, targetWorldPos);
    }

    // Deprecated for the unified map model, but keeping as placeholder if needed later
    public MapId GetRegionalHub(Vector2 physicalPos) => OldNetMapId ?? new MapId(10);
    public void EnsureGlobalMapLoaded(MapId targetMap) { }
}
