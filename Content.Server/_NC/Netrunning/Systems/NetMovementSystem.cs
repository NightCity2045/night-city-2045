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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NetAvatarComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var avatar, out var xform))
        {
            if (xform.GridUid == null) continue;

            if (TryComp<MapGridComponent>(xform.GridUid, out var grid))
            {
                var tile = grid.GetTileRef(xform.Coordinates);
                if (tile.Tile.IsEmpty)
                {
                    // Avatar has stepped into the abyss!
                    HandleAbyssFall(uid, avatar, xform);
                }
            }
        }
    }

    private void HandleAbyssFall(EntityUid uid, NetAvatarComponent avatar, TransformComponent xform)
    {
        // 1. Is this a Local Net? (Linked to a physical body/deck)
        if (avatar.PhysicalBody == null) return;

        // 2. Transition to Global NET
        // We use the physical body's location for geo-routing
        _globalNet.TransitionToGlobal(uid, avatar);
    }
}
