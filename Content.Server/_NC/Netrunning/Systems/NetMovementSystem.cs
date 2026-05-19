using Content.Shared._NC.Netrunning.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Map;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Monitors avatar movement within digital grids.
///     Triggers transitions (e.g. Local to Global) when an avatar enters the "void".
/// </summary>
public sealed class NetMovementSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly NetGlobalSystem _globalNet = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NetAvatarComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var avatar, out var xform))
        {
            // 1. Check if we are on a Net-related map (Local or Global)
            if (xform.GridUid == null)
            {
                if (IsDigitalMap(xform.MapID))
                {
                    HandleAbyssFall(uid, avatar, xform);
                }
                continue;
            }

            // 2. Check if we are on an empty tile on a grid
            if (TryComp<MapGridComponent>(xform.GridUid, out var grid))
            {
                var tile = _mapSystem.GetTileRef(xform.GridUid.Value, grid, xform.Coordinates);
                if (tile.Tile.IsEmpty)
                {
                    HandleAbyssFall(uid, avatar, xform);
                }
            }
        }
    }

    private bool IsDigitalMap(MapId mapId)
    {
        // Global NET
        if (_globalNet.OldNetMapId == mapId) return true;

        // Or is it a Local Net? (Linked to a server)
        var serverQuery = AllEntityQuery<NetServerComponent, TransformComponent>();
        while (serverQuery.MoveNext(out var sUid, out var server, out var sXform))
        {
            if (sXform.MapID == mapId) return true;
        }

        return false;
    }

    private void HandleAbyssFall(EntityUid uid, NetAvatarComponent avatar, TransformComponent xform)
    {
        if (avatar.PhysicalBody == null || _globalNet.OldNetMapId == null) return;

        // If we fall in Local -> Transition to Global
        if (xform.MapID != _globalNet.OldNetMapId)
        {
            _globalNet.TransitionToGlobal(uid, avatar);
        }
    }
}
