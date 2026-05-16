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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetServerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NetServerComponent, ComponentShutdown>(OnShutdown);
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
        // Use TryLoadMap which generates its own ID and avoids "Map already exists"
        if (_mapLoader.TryLoadMap(new ResPath("/Maps/_NC/NET/core_node.yml"), out var map, out _))
        {
            component.DigitalGrid = map.Value.Owner;
        }
    }

    if (component.DigitalGrid == null) return;

        // Cleanup old nodes
        foreach (var node in component.SpawnedNodes)
        {
            QueueDel(node);
        }
        component.SpawnedNodes.Clear();

        // 2. Scan Power Network (LCP/APC)
        if (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || receiver.NetworkLoad.LinkedNetwork == default)
        {
            _popup.PopupEntity("ERROR: Server offline (No LCP link).", uid, PopupType.MediumCaution);
            return;
        }

        var powerNet = receiver.NetworkLoad.LinkedNetwork;
        
        // Find all consumers on the SAME network
        var query = AllEntityQuery<ApcPowerReceiverComponent, TransformComponent>();
        int nodeCount = 0;
        while (query.MoveNext(out var dUid, out var dReceiver, out var dXform))
        {
            if (dReceiver.NetworkLoad.LinkedNetwork == powerNet && dUid != uid)
            {
                // Is it a device we care about?
                if (HasComp<DoorComponent>(dUid) || HasComp<DeviceNetworkComponent>(dUid))
                {
                    SpawnNodeForDevice(component, dUid, nodeCount++);
                }
            }
        }
    }

    private void SpawnNodeForDevice(NetServerComponent server, EntityUid device, int index)
    {
        if (server.DigitalGrid == null) return;

        // Spawn a digital representation
        var x = (index % 4) * 2 - 3;
        var y = (index / 4) * 2 - 3;
        
        var coords = new EntityCoordinates(server.DigitalGrid.Value, x, y);
        var nodeUid = Spawn("NetDataGate", coords); // Using DataGate as placeholder visual
        
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = device;
        
        _metaData.SetEntityName(nodeUid, $"Node: {Name(device)}");
        
        server.SpawnedNodes.Add(nodeUid);
    }
}
