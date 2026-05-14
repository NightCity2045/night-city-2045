using System.Numerics;
using Content.Shared._NC.Netrunning.Components;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Manages the placement and lifecycle of digital grids in the "Net Map".
/// </summary>
public sealed class NetSpatialSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;

    private const int ZoneSpacing = 200; // 200 tiles between centers
    private MapId? _netMap;
    private int _nextZoneIndex = 0;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetNodeComponent, ComponentStartup>(OnNodeStartup);
    }

    private void OnNodeStartup(EntityUid uid, NetNodeComponent component, ComponentStartup args)
    {
        // For persistent hubs, we could restore state here, but for now we wait for first interaction
    }

    public EntityUid GetOrCreateNetGrid(EntityUid nodeUid, NetNodeComponent node)
    {
        if (node.DigitalGrid != null && !Deleted(node.DigitalGrid.Value))
            return node.DigitalGrid.Value;

        // Initialize Net Map if needed
        if (_netMap == null || !_mapManager.MapExists(_netMap.Value))
        {
            _netMap = _mapManager.CreateMap();
            // Default ambient light for netrunners (fully lit)
            var mapUid = _mapManager.GetMapEntityId(_netMap.Value);
            var light = EnsureComp<MapLightComponent>(mapUid);
            light.AmbientLightColor = Color.White;
        }

        // Assign a new zone
        if (node.ZoneIndex == -1)
        {
            node.ZoneIndex = _nextZoneIndex++;
        }

        // Calculate offset (Grid of zones)
        var x = (node.ZoneIndex % 10) * ZoneSpacing;
        var y = (node.ZoneIndex / 10) * ZoneSpacing;
        var offset = new Vector2(x, y);

        // Load base core module
        if (_mapLoader.TryLoadGrid(_netMap.Value, new ResPath("/Maps/_NC/NET/core_node.yml"), out var grid))
        {
            var gridUid = grid.Value.Owner;
            node.DigitalGrid = gridUid;
            
            // Link back and position
            var xform = Transform(gridUid);
            xform.WorldPosition = offset;
            
            Dirty(nodeUid, node);
            return gridUid;
        }

        return EntityUid.Invalid;
    }

    public MapId GetNetMap() 
    {
        if (_netMap == null || !_mapManager.MapExists(_netMap.Value))
        {
            _netMap = _mapManager.CreateMap();
        }
        return _netMap.Value;
    }
}
