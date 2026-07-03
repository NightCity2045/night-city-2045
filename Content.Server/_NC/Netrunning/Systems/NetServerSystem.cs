using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared._NC.Netrunning.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Numerics;
using Content.Server.Doors.Systems;
using Content.Server.Camera;
using Content.Server.DeviceNetwork.Components;
using Robust.Shared.EntitySerialization.Systems;
using Content.Shared.Popups;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Robust.Shared.Maths;
using Robust.Shared.EntitySerialization;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Manages physical servers and their digital network generation.
///     Scans the power network (APC/LCP) for connected devices.
/// </summary>
public sealed class NetServerSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly NetGlobalSystem _globalNet = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly DoorSystem _door = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetServerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NetServerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NetDeviceNodeComponent, ActivateInWorldEvent>(OnNodeActivate);
        SubscribeLocalEvent<NetDeviceNodeComponent, NetNodeControlMessage>(OnControlMessage);
    }

    private void OnNodeActivate(EntityUid uid, NetDeviceNodeComponent component, ActivateInWorldEvent args)
    {
        var physical = component.PhysicalDevice;
        if (Deleted(physical)) return;

        // Ensure netrunner can "see" through the device
        var eyeComp = EnsureComp<EyeComponent>(physical);
        _eye.SetVisibilityMask(physical, 1, eyeComp); // Use int 1 for basic visibility

        _ui.OpenUi(uid, NetNodeUiKey.Key, args.User);
        UpdateNodeUi(uid, component);
    }

    private void UpdateNodeUi(EntityUid uid, NetDeviceNodeComponent component)
    {
        var physical = component.PhysicalDevice;
        var state = new NetNodeUiState(GetNetEntity(physical), Name(physical));
        _ui.SetUiState(uid, NetNodeUiKey.Key, state);
    }

    private void OnControlMessage(EntityUid uid, NetDeviceNodeComponent component, NetNodeControlMessage args)
    {
        var physical = component.PhysicalDevice;
        if (Deleted(physical)) return;

        switch (args.Action)
        {
            case "toggle":
                if (TryComp<DoorComponent>(physical, out var door))
                {
                    _door.TryToggleDoor(physical, door);
                }
                break;
        }
    }

    private void OnMapInit(EntityUid uid, NetServerComponent component, MapInitEvent args)
    {
        RefreshNetwork(uid, component);
        SpawnTowerInGlobalNet(uid, component);
    }

    private void SpawnTowerInGlobalNet(EntityUid serverUid, NetServerComponent component)
    {
        if (_globalNet.OldNetMapId == null) return;
        var targetMap = _globalNet.OldNetMapId.Value;

        var xform = Transform(serverUid);
        var globalPos = xform.WorldPosition;

        // Find the Global Grid (Hub) on the unified map
        var gridQuery = AllEntityQuery<MapGridComponent, TransformComponent>();
        EntityUid globalGridUid = EntityUid.Invalid;
        while (gridQuery.MoveNext(out var gUid, out _, out var gXform))
        {
            if (gXform.MapID == targetMap)
            {
                globalGridUid = gUid;
                break;
            }
        }

        if (globalGridUid == EntityUid.Invalid) return;

        // Spawn the Tower Projection (3x3)
        var centerPos = new EntityCoordinates(globalGridUid, globalPos);
        var portal = Spawn("NetDataGate", centerPos);
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(portal);
        nodeComp.PhysicalDevice = serverUid; 
        _metaData.SetEntityName(portal, $"Access: {Name(serverUid)}");

        // Borders (NetFirewallWall)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var wallCoords = centerPos.Offset(new Vector2(dx, dy));
                Spawn("NetFirewallWall", wallCoords);
            }
        }
    }

    private void OnShutdown(EntityUid uid, NetServerComponent component, ComponentShutdown args)
    {
        if (component.DigitalGrid != null)
        {
            QueueDel(component.DigitalGrid.Value);
        }
    }

    public void RefreshNetwork(EntityUid uid, NetServerComponent component)
    {
        // 1. Create or Clear Digital Grid
        if (component.DigitalGrid == null)
        {
            var options = new DeserializationOptions { InitializeMaps = true };
            if (_mapLoader.TryLoadMap(new ResPath("/Maps/_NC/NET/core_node.yml"), out var map, out var grids, options))
            {
                var mapUid = map.Value.Owner;
                var mapId = map.Value.Comp.MapId;

                // CRITICAL: Store the actual GRID entity, not the map entity
                if (grids != null && grids.Count > 0)
                {
                    component.DigitalGrid = grids.First().Owner;
                }
                else
                {
                    component.DigitalGrid = mapUid;
                }

                // Ensure map is unpaused
                _mapSystem.SetPaused(mapId, false);

                // Ensure environment on the map entity
                var gravity = EnsureComp<GravityComponent>(mapUid);
                gravity.Enabled = true;
                gravity.Inherent = true;

                var light = EnsureComp<MapLightComponent>(mapUid);
                light.AmbientLightColor = Color.White;
                
                Log.Info($"Digital network initialized for server {ToPrettyString(uid)}");
            }
        }

        if (component.DigitalGrid == null) return;

        var hubGridUid = component.DigitalGrid.Value;

        // Force unpause again for safety
        if (TryComp<TransformComponent>(hubGridUid, out var gridXf))
            _mapSystem.SetPaused(gridXf.MapID, false);

        // Cleanup old nodes
        foreach (var node in component.SpawnedNodes)
        {
            QueueDel(node);
        }
        component.SpawnedNodes.Clear();

        // 3. Scan Power Network (LCP/APC)
        if (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || receiver.NetworkLoad.LinkedNetwork == default)
        {
            _popup.PopupEntity("SCAN ERROR: Server has no power link (LCP/APC).", uid, PopupType.MediumCaution);
            return;
        }

        var powerNet = receiver.NetworkLoad.LinkedNetwork;
        int nodeCount = 0;
        
        // Find all consumers on the SAME network
        var deviceQuery = AllEntityQuery<ApcPowerReceiverComponent, TransformComponent>();
        while (deviceQuery.MoveNext(out var dUid, out var dReceiver, out var dXform))
        {
            if (dReceiver.NetworkLoad.LinkedNetwork == powerNet && dUid != uid)
            {
                // Is it a device we care about?
                if (HasComp<DoorComponent>(dUid) || HasComp<DeviceNetworkComponent>(dUid))
                {
                    SpawnNodeForDevice(component, hubGridUid, dUid, nodeCount++);
                }
            }
        }

        if (nodeCount > 0)
        {
            _popup.PopupEntity($"SCAN COMPLETE: {nodeCount} devices mapped.", uid);
        }
        else
        {
            _popup.PopupEntity("SCAN COMPLETE: No network devices found in this LCP segment.", uid);
        }
    }

    private void SpawnNodeForDevice(NetServerComponent server, EntityUid gridUid, EntityUid device, int index)
    {
        // Spread nodes in a 3x3 pattern with 2-tile spacing
        var x = (index % 3) * 2 - 2;
        var y = (index / 3) * 2 - 2;
        
        var coords = new EntityCoordinates(gridUid, x, y);
        var nodeUid = Spawn("NetDeviceNode", coords); 
        
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = device;
        
        _metaData.SetEntityName(nodeUid, $"Node: {Name(device)}");
        
        server.SpawnedNodes.Add(nodeUid);
    }
}
