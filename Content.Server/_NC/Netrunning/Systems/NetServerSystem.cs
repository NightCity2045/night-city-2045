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
using Content.Shared._NC.Power.Components;
using Robust.Shared.Map.Components;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Robust.Shared.Maths;
using Robust.Shared.EntitySerialization;
using Content.Shared.SurveillanceCamera.Components;
using System.Linq;
using Content.Shared.Verbs;

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
        SubscribeLocalEvent<NetDefenseComponent, GetVerbsEvent<UtilityVerb>>(OnDefenseMoveVerbs);
        SubscribeLocalEvent<DefensiveDaemonComponent, GetVerbsEvent<UtilityVerb>>(OnDaemonMoveVerbs);
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
        var deviceName = component.Kind == NetDeviceNodeKind.CameraGroup
            ? $"Camera Control ({component.PhysicalDevices.Count})"
            : Name(physical);
        var state = new NetNodeUiState(GetNetEntity(physical), deviceName, component.Kind, Math.Max(1, component.PhysicalDevices.Count));
        _ui.SetUiState(uid, NetNodeUiKey.Key, state);
    }

    private void OnControlMessage(EntityUid uid, NetDeviceNodeComponent component, NetNodeControlMessage args)
    {
        var physical = component.PhysicalDevice;
        if (Deleted(physical)) return;

        switch (args.Action)
        {
            case "scan":
                if (component.Server is { } scanServer &&
                    TryHasNodeAdminAccess(args.Actor, scanServer) &&
                    TryComp<NetServerComponent>(scanServer, out var serverComp))
                {
                    RefreshNetwork(scanServer, serverComp);
                }
                break;

            case "move_north":
                TryMoveNode(uid, component, args.Actor, new Vector2(0, 1));
                break;

            case "move_south":
                TryMoveNode(uid, component, args.Actor, new Vector2(0, -1));
                break;

            case "move_west":
                TryMoveNode(uid, component, args.Actor, new Vector2(-1, 0));
                break;

            case "move_east":
                TryMoveNode(uid, component, args.Actor, new Vector2(1, 0));
                break;

            case "toggle":
                if (TryComp<DoorComponent>(physical, out var door))
                {
                    _door.TryToggleDoor(physical, door);
                }
                break;
        }
    }

    private void TryMoveNode(EntityUid nodeUid, NetDeviceNodeComponent node, EntityUid actor, Vector2 offset)
    {
        if (node.Server is not { } serverUid || !TryHasNodeAdminAccess(actor, serverUid))
        {
            _popup.PopupEntity("ERROR: Root/admin access required to move network nodes.", nodeUid, actor, PopupType.MediumCaution);
            return;
        }

        var xform = Transform(nodeUid);
        _transform.SetLocalPosition(nodeUid, xform.LocalPosition + offset, xform);
    }

    private void OnDefenseMoveVerbs(EntityUid uid, NetDefenseComponent component, GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.Server is not { } serverUid || !TryHasNodeAdminAccess(args.User, serverUid))
            return;

        AddTopologyMoveVerbs(uid, serverUid, args);
    }

    private void OnDaemonMoveVerbs(EntityUid uid, DefensiveDaemonComponent component, GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var serverUid = ResolveTopologyServer(uid);
        if (serverUid == null || !TryHasNodeAdminAccess(args.User, serverUid.Value))
            return;

        AddTopologyMoveVerbs(uid, serverUid.Value, args);
    }

    private void AddTopologyMoveVerbs(EntityUid uid, EntityUid serverUid, GetVerbsEvent<UtilityVerb> args)
    {
        AddTopologyMoveVerb(uid, serverUid, args, "Move North", new Vector2(0, 1));
        AddTopologyMoveVerb(uid, serverUid, args, "Move South", new Vector2(0, -1));
        AddTopologyMoveVerb(uid, serverUid, args, "Move West", new Vector2(-1, 0));
        AddTopologyMoveVerb(uid, serverUid, args, "Move East", new Vector2(1, 0));
    }

    private void AddTopologyMoveVerb(EntityUid uid, EntityUid serverUid, GetVerbsEvent<UtilityVerb> args, string text, Vector2 offset)
    {
        args.Verbs.Add(new UtilityVerb
        {
            Text = text,
            Category = VerbCategory.Admin,
            Act = () => TryMoveTopologyEntity(uid, serverUid, args.User, offset)
        });
    }

    private void TryMoveTopologyEntity(EntityUid uid, EntityUid serverUid, EntityUid actor, Vector2 offset)
    {
        if (!TryHasNodeAdminAccess(actor, serverUid))
        {
            _popup.PopupEntity("ERROR: Root/admin access required to move topology.", uid, actor, PopupType.MediumCaution);
            return;
        }

        var xform = Transform(uid);
        _transform.SetLocalPosition(uid, xform.LocalPosition + offset, xform);
    }

    private EntityUid? ResolveTopologyServer(EntityUid uid)
    {
        if (TryComp<NetDefenseComponent>(uid, out var defense) && defense.Server is { } defenseServer && !Deleted(defenseServer))
            return defenseServer;

        if (TryComp<NetDeviceNodeComponent>(uid, out var node) && node.Server is { } nodeServer && !Deleted(nodeServer))
            return nodeServer;

        if (TryComp<NetModuleComponent>(uid, out var module) && module.Server is { } moduleServer && !Deleted(moduleServer))
            return moduleServer;

        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || Deleted(gridUid))
            return null;

        if (HasComp<NetServerComponent>(gridUid))
            return gridUid;

        if (TryComp<NetModuleComponent>(gridUid, out var gridModule) && gridModule.Server is { } gridServer && !Deleted(gridServer))
            return gridServer;

        return null;
    }

    private bool TryHasNodeAdminAccess(EntityUid actor, EntityUid serverUid)
    {
        if (!TryComp<NetAvatarComponent>(actor, out var avatar) || avatar.Cyberdeck is not { } deckUid)
            return false;

        if (!TryComp<CyberdeckComponent>(deckUid, out var deck))
            return false;

        return deck.ActiveTarget == serverUid || deck.HackedNetworks.Contains(serverUid);
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
        nodeComp.Kind = NetDeviceNodeKind.DataGate;
        nodeComp.Server = serverUid;
        nodeComp.PhysicalDevices.Clear();
        nodeComp.PhysicalDevices.Add(serverUid);
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

        foreach (var defense in component.SpawnedDefenses)
        {
            if (!Deleted(defense))
                QueueDel(defense);
        }

        component.SpawnedDefenses.Clear();
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

        var devices = CollectNetworkDevices(uid);
        if (devices.Count == 0)
        {
            _popup.PopupEntity("SCAN ERROR: Server has no power link (LCP/APC).", uid, PopupType.MediumCaution);
            return;
        }

        var nodeCount = 0;
        var cameraGroup = new List<EntityUid>();

        foreach (var device in devices)
        {
            if (HasComp<SurveillanceCameraComponent>(device))
            {
                cameraGroup.Add(device);
                continue;
            }

            if (HasComp<DoorComponent>(device))
            {
                SpawnNodeForDevice(uid, component, hubGridUid, device, NetDeviceNodeKind.Door, nodeCount++);
                continue;
            }

            if (HasComp<DeviceNetworkComponent>(device))
                SpawnNodeForDevice(uid, component, hubGridUid, device, NetDeviceNodeKind.Generic, nodeCount++);
        }

        if (cameraGroup.Count > 0)
            SpawnCameraGroupNode(uid, component, hubGridUid, cameraGroup, nodeCount++);

        if (nodeCount > 0)
        {
            _popup.PopupEntity($"SCAN COMPLETE: {nodeCount} devices mapped.", uid);
        }
        else
        {
            _popup.PopupEntity("SCAN COMPLETE: No network devices found in this LCP segment.", uid);
        }
    }

    private HashSet<EntityUid> CollectNetworkDevices(EntityUid serverUid)
    {
        var devices = new HashSet<EntityUid>();

        if (TryComp<ApcPowerReceiverComponent>(serverUid, out var receiver) && receiver.NetworkLoad.LinkedNetwork != default)
        {
            var powerNet = receiver.NetworkLoad.LinkedNetwork;
            var apcQuery = AllEntityQuery<ApcPowerReceiverComponent, TransformComponent>();
            while (apcQuery.MoveNext(out var uid, out var apcReceiver, out _))
            {
                if (uid != serverUid && apcReceiver.NetworkLoad.LinkedNetwork == powerNet)
                    devices.Add(uid);
            }
        }

        if (TryComp<LogicPowerReceiverComponent>(serverUid, out var logicReceiver) && logicReceiver.Provider != null)
            CollectLogicPowerReceivers(serverUid, logicReceiver.Provider.Value, devices);
        else if (TryComp<LogicPowerProviderComponent>(serverUid, out var serverProvider))
            CollectLogicProviderList(serverUid, serverProvider, devices);

        return devices;
    }

    private void CollectLogicPowerReceivers(EntityUid serverUid, EntityUid providerUid, HashSet<EntityUid> devices)
    {
        if (TryComp<LogicPowerProviderComponent>(providerUid, out var provider))
            CollectLogicProviderList(serverUid, provider, devices);

        var logicQuery = AllEntityQuery<LogicPowerReceiverComponent>();
        while (logicQuery.MoveNext(out var uid, out var receiver))
        {
            if (uid != serverUid && receiver.Provider == providerUid)
                devices.Add(uid);
        }
    }

    private void CollectLogicProviderList(EntityUid serverUid, LogicPowerProviderComponent provider, HashSet<EntityUid> devices)
    {
        foreach (var receiverUid in provider.Receivers)
        {
            if (receiverUid != serverUid && !Deleted(receiverUid))
                devices.Add(receiverUid);
        }
    }

    private void SpawnNodeForDevice(EntityUid serverUid, NetServerComponent server, EntityUid gridUid, EntityUid device, NetDeviceNodeKind kind, int index)
    {
        // Spread nodes in a 3x3 pattern with 2-tile spacing
        var x = (index % 3) * 2 - 2;
        var y = (index / 3) * 2 - 2;
        
        var coords = new EntityCoordinates(gridUid, x, y);
        var nodeUid = Spawn("NetDeviceNode", coords); 
        
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = device;
        nodeComp.Kind = kind;
        nodeComp.Server = serverUid;
        nodeComp.PhysicalDevices.Clear();
        nodeComp.PhysicalDevices.Add(device);
        
        _metaData.SetEntityName(nodeUid, $"Node: {Name(device)}");
        
        server.SpawnedNodes.Add(nodeUid);
    }

    private void SpawnCameraGroupNode(EntityUid serverUid, NetServerComponent server, EntityUid gridUid, List<EntityUid> cameras, int index)
    {
        var x = (index % 3) * 2 - 2;
        var y = (index / 3) * 2 - 2;

        var nodeUid = Spawn("NetDeviceNode", new EntityCoordinates(gridUid, x, y));
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = cameras[0];
        nodeComp.Kind = NetDeviceNodeKind.CameraGroup;
        nodeComp.Server = serverUid;
        nodeComp.PhysicalDevices.Clear();
        nodeComp.PhysicalDevices.AddRange(cameras);

        _metaData.SetEntityName(nodeUid, $"Node: Camera Control ({cameras.Count})");
        server.SpawnedNodes.Add(nodeUid);
    }
}
