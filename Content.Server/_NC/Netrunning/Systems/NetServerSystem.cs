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
using Content.Shared.Interaction.Events;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared._NC.Netrunning.Prototypes;
using Content.Server.Light.Components;
using Robust.Shared.Containers;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Content.Server.SurveillanceCamera;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Manages physical servers and their digital network generation.
///     Scans the power network (APC/LCP) for connected devices.
/// </summary>
public sealed class NetServerSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, EntityUid> _draggedTopologyByAvatar = new();

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
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MetaProgramSystem _metaProgram = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscribers = default!;
    [Dependency] private readonly SurveillanceCameraSystem _surveillanceCameras = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetServerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NetServerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NetServerComponent, BoundUIOpenedEvent>(OnServerUiOpened);
        SubscribeLocalEvent<NetServerComponent, ActivateInWorldEvent>(OnServerActivate);
        SubscribeLocalEvent<NetServerComponent, InteractHandEvent>(OnServerInteractHand);
        SubscribeLocalEvent<NetServerComponent, InteractUsingEvent>(OnServerInteractUsing);
        SubscribeLocalEvent<NetServerComponent, NetServerScanMessage>(OnServerScanMessage);
        SubscribeLocalEvent<NetServerComponent, NetServerConstructMessage>(OnServerConstructMessage);
        SubscribeLocalEvent<NetServerComponent, NetServerAdminMessage>(OnServerAdminMessage);
        SubscribeLocalEvent<NetServerComponent, NetServerTopologyMoveMessage>(OnServerTopologyMoveMessage);
        SubscribeLocalEvent<NetServerComponent, GetVerbsEvent<ActivationVerb>>(OnServerVerbs);
        SubscribeLocalEvent<NetAvatarComponent, MoveEvent>(OnAvatarMove);
        SubscribeLocalEvent<NetAvatarComponent, StartPullAttemptEvent>(OnAvatarStartPullAttempt);
        SubscribeLocalEvent<NetAvatarComponent, ComponentShutdown>(OnAvatarShutdown);
        SubscribeLocalEvent<NetDeviceNodeComponent, ActivateInWorldEvent>(OnNodeActivate);
        SubscribeLocalEvent<NetDeviceNodeComponent, BoundUIOpenedEvent>(OnNodeUiOpened);
        SubscribeLocalEvent<NetDeviceNodeComponent, BoundUIClosedEvent>(OnNodeUiClosed);
        SubscribeLocalEvent<NetDeviceNodeComponent, ComponentShutdown>(OnNodeShutdown);
        SubscribeLocalEvent<NetDeviceNodeComponent, BeingPulledAttemptEvent>(OnNodeBeingPulledAttempt);
        SubscribeLocalEvent<NetDeviceNodeComponent, AttemptStopPullingEvent>(OnTopologyStopPullingAttempt);
        SubscribeLocalEvent<NetDeviceNodeComponent, NetNodeControlMessage>(OnControlMessage);
        SubscribeLocalEvent<NetDeviceNodeComponent, NetNodeExecuteShardMessage>(OnExecuteShardMessage);
        SubscribeLocalEvent<NetDefenseComponent, BeingPulledAttemptEvent>(OnDefenseBeingPulledAttempt);
        SubscribeLocalEvent<NetDefenseComponent, AttemptStopPullingEvent>(OnTopologyStopPullingAttempt);
        SubscribeLocalEvent<NetDataGateComponent, BeingPulledAttemptEvent>(OnDataGateBeingPulledAttempt);
        SubscribeLocalEvent<NetDataGateComponent, AttemptStopPullingEvent>(OnTopologyStopPullingAttempt);
        SubscribeLocalEvent<NetDefenseComponent, GetVerbsEvent<UtilityVerb>>(OnDefenseMoveVerbs);
        SubscribeLocalEvent<DefensiveDaemonComponent, GetVerbsEvent<UtilityVerb>>(OnDaemonMoveVerbs);
    }

    private void OnServerActivate(EntityUid uid, NetServerComponent component, ActivateInWorldEvent args)
    {
        OpenServerUi(uid, component, args.User);
        args.Handled = true;
    }

    private void OnServerInteractHand(EntityUid uid, NetServerComponent component, InteractHandEvent args)
    {
        OpenServerUi(uid, component, args.User);
        args.Handled = true;
    }

    private void OpenServerUi(EntityUid uid, NetServerComponent component, EntityUid user)
    {
        _ui.OpenUi(uid, NetServerUiKey.Key, user);
        UpdateServerUi(uid, component, user);
    }

    private void OnServerUiOpened(EntityUid uid, NetServerComponent component, BoundUIOpenedEvent args)
    {
        UpdateServerUi(uid, component, args.Actor);
    }

    private void OnServerInteractUsing(EntityUid uid, NetServerComponent component, InteractUsingEvent args)
    {
        if (!TryComp<DataShardComponent>(args.Used, out var shard) || shard.ProgramKind != MetaProgramKind.DaemonDefensive)
            return;

        if (!_containers.TryGetContainer(uid, NetServerComponent.DaemonShardContainerId, out var container))
            return;

        if (container.ContainedEntities.Count > 0)
        {
            _popup.PopupEntity("Слот защитного демона уже занят.", uid, args.User, PopupType.MediumCaution);
            args.Handled = true;
            return;
        }

        if (_containers.Insert(args.Used, container))
        {
            _popup.PopupEntity("Защитный META-шард установлен в сервер.", uid, args.User);
            UpdateServerUi(uid, component, args.User);
            args.Handled = true;
        }
    }

    private void OnServerVerbs(EntityUid uid, NetServerComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new ActivationVerb
        {
            Text = "Открыть консоль сервера",
            Act = () => OpenServerUi(uid, component, args.User)
        });

        if (!_containers.TryGetContainer(uid, NetServerComponent.DaemonShardContainerId, out var container) ||
            container.ContainedEntities.Count == 0)
            return;

        var installed = container.ContainedEntities[0];
        args.Verbs.Add(new ActivationVerb
        {
            Text = "Извлечь защитный шард",
            Act = () =>
            {
                if (_containers.Remove(installed, container))
                {
                    _popup.PopupEntity("Защитный META-шард извлечен.", uid, args.User);
                    UpdateServerUi(uid, component, args.User);
                }
            }
        });
    }

    private void OnServerScanMessage(EntityUid uid, NetServerComponent component, NetServerScanMessage args)
    {
        RefreshNetwork(uid, component);
        UpdateServerUi(uid, component, args.Actor);
    }

    private void OnServerConstructMessage(EntityUid uid, NetServerComponent component, NetServerConstructMessage args)
    {
        if (TryConstructModule(uid, component, args.Actor, args.ModuleId, args.Anchor))
            UpdateServerUi(uid, component, args.Actor);
    }

    private void OnServerAdminMessage(EntityUid uid, NetServerComponent component, NetServerAdminMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TryResolveLinkedDeck(args.Actor, uid, out var deckUid, out var deck))
        {
            _popup.PopupEntity("Сначала свяжи свою деку с этим сервером.", uid, args.Actor, PopupType.MediumCaution);
            UpdateServerUi(uid, component, args.Actor);
            return;
        }

        if (deck.HackedNetworks.Contains(uid))
        {
            _popup.PopupEntity("Рут-доступ уже получен и сохранен в деке.", uid, args.Actor);
            UpdateServerUi(uid, component, args.Actor);
            _metaProgram.UpdateUi(deckUid, deck, args.Actor);
            return;
        }

        if (deck.ActiveTarget == uid)
        {
            _popup.PopupEntity("Локальный админ-сеанс уже активен.", uid, args.Actor);
            UpdateServerUi(uid, component, args.Actor);
            _metaProgram.UpdateUi(deckUid, deck, args.Actor);
            return;
        }

        deck.ActiveServer = uid;
        deck.ActiveTarget = uid;
        Dirty(deckUid, deck);

        _popup.PopupEntity("Локальный админ-сеанс открыт. Топология разблокирована.", uid, args.Actor);
        UpdateServerUi(uid, component, args.Actor);
        _metaProgram.UpdateUi(deckUid, deck, args.Actor);
    }

    private void OnServerTopologyMoveMessage(EntityUid uid, NetServerComponent component, NetServerTopologyMoveMessage args)
    {
        if (!args.Actor.Valid)
            return;

        var targetUid = GetEntity(args.Target);
        if (Deleted(targetUid) || ResolveTopologyServer(targetUid) != uid)
        {
            _popup.PopupEntity("ОШИБКА: выбранный узел больше не принадлежит этой локальной сети.", uid, args.Actor, PopupType.MediumCaution);
            UpdateServerUi(uid, component, args.Actor);
            return;
        }

        if (TryMoveTopologyEntityToTile(targetUid, uid, args.Actor, args.Tile))
            UpdateServerUi(uid, component, args.Actor);
    }

    private void OnNodeActivate(EntityUid uid, NetDeviceNodeComponent component, ActivateInWorldEvent args)
    {
        var physical = component.PhysicalDevice;
        if (Deleted(physical)) return;

        if (TryResolveActorDeck(args.User, out var deckUid, out var deck))
        {
            deck.ActiveTarget = physical;
            Dirty(deckUid, deck);
            _metaProgram.UpdateUi(deckUid, deck, args.User);
        }

        _ui.OpenUi(uid, NetNodeUiKey.Key, args.User);
        UpdateNodeUi(uid, component, args.User);
    }

    private void OnNodeUiOpened(EntityUid uid, NetDeviceNodeComponent component, BoundUIOpenedEvent args)
    {
        SubscribeNodeViewer(uid, component, args.Actor);
        UpdateNodeUi(uid, component, args.Actor);
    }

    private void OnNodeUiClosed(EntityUid uid, NetDeviceNodeComponent component, BoundUIClosedEvent args)
    {
        UnsubscribeNodeViewer(uid, component, args.Actor);
    }

    private void OnNodeShutdown(EntityUid uid, NetDeviceNodeComponent component, ComponentShutdown args)
    {
        ClearDraggedTopologyTarget(uid);

        foreach (var viewer in component.ActiveViewers.ToArray())
        {
            UnsubscribeNodeViewer(uid, component, viewer);
        }
    }

    private void OnAvatarShutdown(EntityUid uid, NetAvatarComponent component, ComponentShutdown args)
    {
        _draggedTopologyByAvatar.Remove(uid);
    }

    private void OnAvatarStartPullAttempt(EntityUid uid, NetAvatarComponent component, ref StartPullAttemptEvent args)
    {
        if (!TryGetTopologyPullServer(args.Pulled, out var serverUid) || !TryHasNodeAdminAccess(uid, serverUid))
            return;

        // Topology dragging uses the existing pull gesture, but movement is handled
        // manually because digital nodes are intentionally pass-through entities.
        if (_draggedTopologyByAvatar.TryGetValue(uid, out var currentlyDragged) && currentlyDragged == args.Pulled)
        {
            _draggedTopologyByAvatar.Remove(uid);
            args.Cancel();
            return;
        }

        _draggedTopologyByAvatar[uid] = args.Pulled;
        args.Cancel();
    }

    private void OnAvatarMove(EntityUid uid, NetAvatarComponent component, ref MoveEvent args)
    {
        if (!_draggedTopologyByAvatar.TryGetValue(uid, out var pulledUid) ||
            Deleted(pulledUid) ||
            !TryGetTopologyPullServer(pulledUid, out var serverUid) ||
            !TryHasNodeAdminAccess(uid, serverUid))
        {
            _draggedTopologyByAvatar.Remove(uid);
            return;
        }

        // Keep dragged topology snapped to the avatar's previous tile so manual
        // net-topology rearranging works on pass-through digital entities.
        if (args.ParentChanged)
            return;

        var pulledXform = Transform(pulledUid);
        if (pulledXform.MapID != args.OldPosition.ToMap(EntityManager, _transform).MapId)
            return;

        _transform.SetCoordinates(pulledUid, pulledXform, args.OldPosition);
    }

    private void OnTopologyStopPullingAttempt<TComponent>(EntityUid uid, TComponent component, ref AttemptStopPullingEvent args)
        where TComponent : IComponent
    {
        if (args.User is not { } user)
            return;

        if (_draggedTopologyByAvatar.TryGetValue(user, out var dragged) && dragged == uid)
            _draggedTopologyByAvatar.Remove(user);
    }

    private void OnNodeBeingPulledAttempt(EntityUid uid, NetDeviceNodeComponent component, BeingPulledAttemptEvent args)
    {
        // Digital topology may only be rearranged by a netrunner who currently holds
        // admin/root authority over the owning local server.
        if (component.Server is not { } serverUid || !TryHasNodeAdminAccess(args.Puller, serverUid))
            args.Cancel();
    }

    private void OnDefenseBeingPulledAttempt(EntityUid uid, NetDefenseComponent component, BeingPulledAttemptEvent args)
    {
        if (component.Server is not { } serverUid || !TryHasNodeAdminAccess(args.Puller, serverUid))
            args.Cancel();
    }

    private void OnDataGateBeingPulledAttempt(EntityUid uid, NetDataGateComponent component, BeingPulledAttemptEvent args)
    {
        if (!TryComp<NetDeviceNodeComponent>(uid, out var node) ||
            node.Server is not { } serverUid ||
            !TryHasNodeAdminAccess(args.Puller, serverUid))
        {
            args.Cancel();
        }
    }

    private void UpdateNodeUi(EntityUid uid, NetDeviceNodeComponent component, EntityUid? user = null)
    {
        var physical = component.PhysicalDevice;
        var deviceName = component.Kind == NetDeviceNodeKind.CameraGroup
            ? $"Управление камерами ({component.PhysicalDevices.Count})"
            : Name(physical);
        var hasLinkedDeck = false;
        var shards = new List<NetNodeShardInfo>();

        if (user is { } actor && TryResolveActorDeck(actor, out var deckUid, out _))
        {
            hasLinkedDeck = true;
            if (_containers.TryGetContainer(deckUid, CyberdeckComponent.ShardContainerId, out var shardContainer))
            {
                foreach (var shardUid in shardContainer.ContainedEntities)
                {
                    if (!TryComp<DataShardComponent>(shardUid, out var shard) || shard.ProgramKind == MetaProgramKind.DaemonDefensive)
                        continue;

                    shards.Add(new NetNodeShardInfo(GetNetEntity(shardUid), Name(shardUid), shard.RequiredRam, shard.ProgramKind));
                }
            }
        }

        var state = new NetNodeUiState(
            GetNetEntity(physical),
            deviceName,
            component.Kind,
            Math.Max(1, component.PhysicalDevices.Count),
            hasLinkedDeck,
            shards);
        _ui.SetUiState(uid, NetNodeUiKey.Key, state);
    }

    private void UpdateServerUi(EntityUid uid, NetServerComponent component, EntityUid? user = null)
    {
        var modules = new List<NetModuleInfo>();
        foreach (var proto in _proto.EnumeratePrototypes<NetModulePrototype>())
        {
            modules.Add(new NetModuleInfo(proto.ID, proto.Name, proto.Description, proto.RamCost, proto.Price));
        }

        var anchors = new List<NetAnchorInfo>();
        if (component.DigitalGrid is { } gridUid && !Deleted(gridUid))
        {
            var xformQuery = GetEntityQuery<TransformComponent>();
            if (xformQuery.TryGetComponent(gridUid, out var gridXform))
            {
                var mapId = gridXform.MapID;
                var gridPos = gridXform.WorldPosition;
                var query = AllEntityQuery<NetAnchorComponent, TransformComponent>();
                while (query.MoveNext(out var anchorUid, out var anchor, out var xform))
                {
                    if (xform.MapID == mapId && (xform.WorldPosition - gridPos).Length() < 150f)
                        anchors.Add(new NetAnchorInfo(GetNetEntity(anchorUid), anchor.Direction, anchor.Connected));
                }
            }
        }

        var devices = new List<NetServerDeviceInfo>();
        foreach (var deviceUid in CollectNetworkDevices(uid))
        {
            if (Deleted(deviceUid))
                continue;

            devices.Add(new NetServerDeviceInfo(GetNetEntity(deviceUid), Name(deviceUid), GetDeviceClass(deviceUid)));
        }

        var hasDaemonShard =
            _containers.TryGetContainer(uid, NetServerComponent.DaemonShardContainerId, out var daemonContainer) &&
            daemonContainer.ContainedEntities.Count > 0;

        var providerLabel = "ЛКП: нет";
        if (TryComp<LogicPowerReceiverComponent>(uid, out var logicReceiver) && logicReceiver.Provider is { } providerUid && !Deleted(providerUid))
            providerLabel = $"ЛКП: {Name(providerUid)}";
        else if (TryComp<LogicPowerProviderComponent>(uid, out _))
            providerLabel = $"ЛКП: {Name(uid)}";

        var hasAdminAccess = false;
        var hasPersistentRoot = false;
        var canRequestAdmin = false;
        var accessStatus = "ДОСТУП: НЕТ СЕАНСА";

        if (user is { } actor && TryResolveLinkedDeck(actor, uid, out _, out var deck))
        {
            canRequestAdmin = true;
            hasPersistentRoot = deck.HackedNetworks.Contains(uid);
            hasAdminAccess = deck.ActiveTarget == uid || hasPersistentRoot;

            accessStatus = hasPersistentRoot
                ? "ДОСТУП: ROOT / ПОСТОЯННЫЙ"
                : deck.ActiveTarget == uid
                    ? "ДОСТУП: ЛОКАЛЬНЫЙ АДМИН"
                    : "ДОСТУП: ДЕКА СВЯЗАНА, СЕАНС НЕ ОТКРЫТ";
        }

        var topologyEntries = BuildTopologyEntries(uid, component, out var topologyMinTile, out var topologyMaxTile);

        var state = new NetServerUiState(
            $"СЕРВЕР://{Name(uid).ToUpperInvariant()}",
            providerLabel,
            component.UsedLoad,
            component.MaxLoad,
            component.SpawnedModules.Count,
            component.MaxModules,
            devices.Count,
            hasDaemonShard,
            hasAdminAccess,
            hasPersistentRoot,
            canRequestAdmin,
            accessStatus,
            topologyMinTile,
            topologyMaxTile,
            modules,
            anchors,
            devices,
            topologyEntries);

        _ui.SetUiState(uid, NetServerUiKey.Key, state);
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

    private void OnExecuteShardMessage(EntityUid uid, NetDeviceNodeComponent component, NetNodeExecuteShardMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TryResolveActorDeck(args.Actor, out var deckUid, out var deck))
        {
            _popup.PopupEntity("ОШИБКА: дека нетраннера не найдена.", uid, args.Actor, PopupType.MediumCaution);
            UpdateNodeUi(uid, component, args.Actor);
            return;
        }

        deck.ActiveTarget = component.PhysicalDevice;
        Dirty(deckUid, deck);

        var shardUid = GetEntity(args.Shard);
        if (!TryComp<DataShardComponent>(shardUid, out var shard))
        {
            _popup.PopupEntity("ОШИБКА: шард недоступен.", uid, args.Actor, PopupType.MediumCaution);
            return;
        }

        if (shard.Bytecode == null && !_metaProgram.TryCompile(shardUid, shard, args.Actor, out var compileError))
        {
            _popup.PopupEntity($"ОШИБКА КОМПИЛЯЦИИ: {compileError}", uid, args.Actor, PopupType.MediumCaution);
            return;
        }

        var result = _metaProgram.Execute(deckUid, deck, shardUid, shard);
        if (result.FatalError != null)
            _popup.PopupEntity(result.FatalError, uid, args.Actor, PopupType.MediumCaution);

        _metaProgram.UpdateUi(deckUid, deck, args.Actor);
        UpdateNodeUi(uid, component, args.Actor);
    }

    private void SubscribeNodeViewer(EntityUid nodeUid, NetDeviceNodeComponent component, EntityUid viewer)
    {
        if (component.ActiveViewers.Contains(viewer))
            return;

        var physical = component.PhysicalDevice;
        if (Deleted(physical) || !TryComp<ActorComponent>(viewer, out var actor))
            return;

        if (component.Kind == NetDeviceNodeKind.CameraGroup && TryComp<SurveillanceCameraComponent>(physical, out var cameraComp))
        {
            _surveillanceCameras.AddActiveViewer(physical, viewer, nodeUid, cameraComp, actor);
        }
        else
        {
            _viewSubscribers.AddViewSubscriber(physical, actor.PlayerSession);
        }

        component.ActiveViewers.Add(viewer);
    }

    private void UnsubscribeNodeViewer(EntityUid nodeUid, NetDeviceNodeComponent component, EntityUid viewer)
    {
        if (!component.ActiveViewers.Remove(viewer))
            return;

        var physical = component.PhysicalDevice;
        if (Deleted(physical) || !TryComp<ActorComponent>(viewer, out var actor))
            return;

        if (component.Kind == NetDeviceNodeKind.CameraGroup && TryComp<SurveillanceCameraComponent>(physical, out var cameraComp))
        {
            _surveillanceCameras.RemoveActiveViewer(physical, viewer, nodeUid, cameraComp, actor);
        }
        else
        {
            _viewSubscribers.RemoveViewSubscriber(physical, actor.PlayerSession);
        }
    }

    private void TryMoveNode(EntityUid nodeUid, NetDeviceNodeComponent node, EntityUid actor, Vector2 offset)
    {
        if (node.Server is not { } serverUid || !TryHasNodeAdminAccess(actor, serverUid))
        {
            _popup.PopupEntity("ERROR: Root/admin access required to pull network nodes.", nodeUid, actor, PopupType.MediumCaution);
            return;
        }

        if (!TryGetTopologyTile(nodeUid, out _, out var tile))
            return;

        var delta = new Vector2i((int) offset.X, (int) offset.Y);
        TryMoveTopologyEntityToTile(nodeUid, serverUid, actor, tile + delta);
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
        AddTopologyMoveVerb(uid, serverUid, args, "Pull North", new Vector2(0, 1));
        AddTopologyMoveVerb(uid, serverUid, args, "Pull South", new Vector2(0, -1));
        AddTopologyMoveVerb(uid, serverUid, args, "Pull West", new Vector2(-1, 0));
        AddTopologyMoveVerb(uid, serverUid, args, "Pull East", new Vector2(1, 0));
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
            _popup.PopupEntity("ERROR: Root/admin access required to pull topology.", uid, actor, PopupType.MediumCaution);
            return;
        }

        if (!TryGetTopologyTile(uid, out _, out var tile))
            return;

        var delta = new Vector2i((int) offset.X, (int) offset.Y);
        TryMoveTopologyEntityToTile(uid, serverUid, actor, tile + delta);
    }

    private List<NetTopologyMapEntry> BuildTopologyEntries(EntityUid serverUid, NetServerComponent component, out Vector2i minTile, out Vector2i maxTile)
    {
        minTile = new Vector2i(-4, -4);
        maxTile = new Vector2i(4, 4);

        var entries = new List<NetTopologyMapEntry>();
        var hadAny = false;

        foreach (var nodeUid in component.SpawnedNodes)
        {
            if (Deleted(nodeUid) || !TryGetTopologyTile(nodeUid, out _, out var tile))
                continue;

            hadAny = true;
            ExpandTopologyBounds(tile, ref minTile, ref maxTile);

            var className = "УЗЕЛ";
            if (TryComp<NetDeviceNodeComponent>(nodeUid, out var nodeComp))
            {
                className = nodeComp.Kind == NetDeviceNodeKind.CameraGroup
                    ? "КАМЕРЫ"
                    : GetDeviceClass(nodeComp.PhysicalDevice);
            }

            entries.Add(new NetTopologyMapEntry(GetNetEntity(nodeUid), Name(nodeUid), className, tile));
        }

        foreach (var defenseUid in component.SpawnedDefenses)
        {
            if (Deleted(defenseUid) || !TryGetTopologyTile(defenseUid, out _, out var tile))
                continue;

            hadAny = true;
            ExpandTopologyBounds(tile, ref minTile, ref maxTile);
            entries.Add(new NetTopologyMapEntry(GetNetEntity(defenseUid), Name(defenseUid), "ЛЁД", tile));
        }

        if (!hadAny)
            return entries;

        minTile -= new Vector2i(2, 2);
        maxTile += new Vector2i(2, 2);
        return entries;
    }

    private void ExpandTopologyBounds(Vector2i tile, ref Vector2i minTile, ref Vector2i maxTile)
    {
        minTile = new Vector2i(Math.Min(minTile.X, tile.X), Math.Min(minTile.Y, tile.Y));
        maxTile = new Vector2i(Math.Max(maxTile.X, tile.X), Math.Max(maxTile.Y, tile.Y));
    }

    private bool TryGetTopologyTile(EntityUid uid, out EntityUid gridUid, out Vector2i tile)
    {
        gridUid = EntityUid.Invalid;
        tile = default;

        var xform = Transform(uid);
        if (xform.GridUid is not { } entityGridUid || Deleted(entityGridUid) || !TryComp<MapGridComponent>(entityGridUid, out var grid))
            return false;

        gridUid = entityGridUid;
        tile = _mapSystem.CoordinatesToTile(entityGridUid, grid, xform.Coordinates);
        return true;
    }

    private bool TryMoveTopologyEntityToTile(EntityUid uid, EntityUid serverUid, EntityUid actor, Vector2i targetTile)
    {
        if (!TryHasNodeAdminAccess(actor, serverUid))
        {
            _popup.PopupEntity("ОШИБКА: для перестройки топологии нужен локальный admin или ROOT.", uid, actor, PopupType.MediumCaution);
            return false;
        }

        if (!TryGetTopologyTile(uid, out var gridUid, out var currentTile) || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        if (currentTile == targetTile)
            return true;

        if (!grid.TryGetTileRef(targetTile, out var tileRef) || tileRef.Tile.IsEmpty)
        {
            _popup.PopupEntity("ОШИБКА: выбранный тайл находится вне развернутой карты локальной сети.", uid, actor, PopupType.MediumCaution);
            return false;
        }

        if (!IsTopologyTileFree(serverUid, uid, gridUid, targetTile))
        {
            _popup.PopupEntity("ОШИБКА: тайл уже занят другим узлом или защитой.", uid, actor, PopupType.MediumCaution);
            return false;
        }

        var targetCoords = _mapSystem.GridTileToLocal(gridUid, grid, targetTile);
        _transform.SetCoordinates(uid, targetCoords);

        if (TryComp<NetServerComponent>(serverUid, out var server) &&
            TryComp<NetDeviceNodeComponent>(uid, out var nodeComp))
        {
            RememberNodeLayout(server, nodeComp, targetTile);
        }

        return true;
    }

    private bool IsTopologyTileFree(EntityUid serverUid, EntityUid movingUid, EntityUid gridUid, Vector2i targetTile)
    {
        if (!TryComp<NetServerComponent>(serverUid, out var server))
            return false;

        foreach (var nodeUid in server.SpawnedNodes)
        {
            if (nodeUid == movingUid || Deleted(nodeUid))
                continue;

            if (TryGetTopologyTile(nodeUid, out var otherGridUid, out var otherTile) &&
                otherGridUid == gridUid &&
                otherTile == targetTile)
            {
                return false;
            }
        }

        foreach (var defenseUid in server.SpawnedDefenses)
        {
            if (defenseUid == movingUid || Deleted(defenseUid))
                continue;

            if (TryGetTopologyTile(defenseUid, out var otherGridUid, out var otherTile) &&
                otherGridUid == gridUid &&
                otherTile == targetTile)
            {
                return false;
            }
        }

        return true;
    }

    private void RememberNodeLayout(NetServerComponent server, NetDeviceNodeComponent node, Vector2i tile)
    {
        var key = GetNodeLayoutKey(node);
        if (key != null)
            server.NodeLayout[key] = tile;
    }

    private Vector2i GetDefaultNodeTile(int index)
    {
        var x = (index % 3) * 2 - 2;
        var y = (index / 3) * 2 - 2;
        return new Vector2i(x, y);
    }

    private Vector2i GetNodeSpawnTile(NetServerComponent server, string layoutKey, int index)
    {
        if (server.NodeLayout.TryGetValue(layoutKey, out var storedTile))
            return storedTile;

        return GetDefaultNodeTile(index);
    }

    private string? GetNodeLayoutKey(NetDeviceNodeComponent node)
    {
        if (node.Kind == NetDeviceNodeKind.CameraGroup)
            return "camera_group";

        if (!node.PhysicalDevice.Valid)
            return null;

        return $"device:{node.PhysicalDevice}";
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

    private bool TryGetTopologyPullServer(EntityUid uid, out EntityUid serverUid)
    {
        serverUid = EntityUid.Invalid;

        if (TryComp<DefensiveDaemonComponent>(uid, out _))
            return false;

        var resolved = ResolveTopologyServer(uid);
        if (resolved == null)
            return false;

        serverUid = resolved.Value;
        return true;
    }

    private void ClearDraggedTopologyTarget(EntityUid targetUid)
    {
        foreach (var (avatarUid, draggedUid) in _draggedTopologyByAvatar.ToArray())
        {
            if (draggedUid == targetUid)
                _draggedTopologyByAvatar.Remove(avatarUid);
        }
    }

    private bool TryHasNodeAdminAccess(EntityUid actor, EntityUid serverUid)
    {
        if (!TryComp<NetAvatarComponent>(actor, out var avatar) || avatar.Cyberdeck is not { } deckUid)
            return false;

        if (!TryComp<CyberdeckComponent>(deckUid, out var deck))
            return false;

        return deck.ActiveTarget == serverUid || deck.HackedNetworks.Contains(serverUid);
    }

    private bool TryResolveLinkedDeck(EntityUid actor, EntityUid serverUid, out EntityUid deckUid, out CyberdeckComponent deck)
    {
        deckUid = EntityUid.Invalid;
        deck = default!;

        if (TryComp<HandsComponent>(actor, out var hands) &&
            hands.ActiveHandEntity is { } held &&
            TryComp<CyberdeckComponent>(held, out var heldDeck) &&
            heldDeck.ActiveServer == serverUid)
        {
            deckUid = held;
            deck = heldDeck;
            return true;
        }

        var enumerator = _inventory.GetSlotEnumerator(actor);
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp<CyberdeckComponent>(item, out var invDeck) || invDeck.ActiveServer != serverUid)
                continue;

            deckUid = item;
            deck = invDeck;
            return true;
        }

        return false;
    }

    private bool TryResolveActorDeck(EntityUid actor, out EntityUid deckUid, out CyberdeckComponent deck)
    {
        deckUid = EntityUid.Invalid;
        deck = default!;

        if (TryComp<NetAvatarComponent>(actor, out var avatar) &&
            avatar.Cyberdeck is { } avatarDeckUid &&
            !Deleted(avatarDeckUid) &&
            TryComp<CyberdeckComponent>(avatarDeckUid, out var avatarDeck))
        {
            deckUid = avatarDeckUid;
            deck = avatarDeck;
            return true;
        }

        if (TryComp<HandsComponent>(actor, out var hands) &&
            hands.ActiveHandEntity is { } held &&
            TryComp<CyberdeckComponent>(held, out var heldDeck))
        {
            deckUid = held;
            deck = heldDeck;
            return true;
        }

        var enumerator = _inventory.GetSlotEnumerator(actor);
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp<CyberdeckComponent>(item, out var invDeck))
                continue;

            deckUid = item;
            deck = invDeck;
            return true;
        }

        return false;
    }

    private bool TryConstructModule(EntityUid uid, NetServerComponent component, EntityUid user, string moduleId, NetEntity anchorNet)
    {
        if (component.DigitalGrid == null)
        {
            _popup.PopupEntity("ОШИБКА: у сервера нет инициализированной цифровой решетки.", uid, user, PopupType.MediumCaution);
            return false;
        }

        if (!_proto.TryIndex<NetModulePrototype>(moduleId, out var module))
            return false;

        if (component.SpawnedModules.Count >= component.MaxModules)
        {
            _popup.PopupEntity($"ОШИБКА: достигнут лимит модулей сервера ({component.MaxModules}).", uid, user, PopupType.MediumCaution);
            return false;
        }

        if (component.UsedLoad + module.RamCost > component.MaxLoad)
        {
            _popup.PopupEntity($"ОШИБКА: перегрузка сервера ({component.UsedLoad + module.RamCost}/{component.MaxLoad}).", uid, user, PopupType.MediumCaution);
            return false;
        }

        var gridUid = component.DigitalGrid.Value;
        if (!HasComp<MapGridComponent>(gridUid))
            return false;

        var targetAnchorUid = GetEntity(anchorNet);
        if (!TryComp<NetAnchorComponent>(targetAnchorUid, out var targetAnchor) || targetAnchor.Connected)
        {
            _popup.PopupEntity("ОШИБКА: порт расширения недоступен или уже занят.", uid, user, PopupType.MediumCaution);
            return false;
        }

        var targetXform = Transform(targetAnchorUid);
        if (!_mapLoader.TryLoadGrid(targetXform.MapID, new ResPath(module.TemplatePath), out var newModuleGrid))
            return false;

        var loadedGridUid = newModuleGrid.Value.Owner;
        var oppositeDir = GetOppositeDirection(targetAnchor.Direction);
        EntityUid? entryAnchorUid = null;

        var anchorQuery = AllEntityQuery<NetAnchorComponent, TransformComponent>();
        while (anchorQuery.MoveNext(out var aUid, out var anchor, out var xform))
        {
            if (xform.ParentUid == loadedGridUid && anchor.Direction == oppositeDir)
            {
                entryAnchorUid = aUid;
                break;
            }
        }

        var entryRelativePos = Vector2.Zero;
        if (entryAnchorUid != null)
            entryRelativePos = Transform(entryAnchorUid.Value).LocalPosition;

        var targetWorldPos = targetXform.WorldPosition;
        var spawnWorldPos = targetWorldPos - entryRelativePos;
        Transform(loadedGridUid).WorldPosition = spawnWorldPos;

        var modComp = EnsureComp<NetModuleComponent>(loadedGridUid);
        modComp.PrototypeId = module.ID;
        modComp.ReservedLoad = module.RamCost;
        modComp.Server = uid;

        targetAnchor.Connected = true;
        if (entryAnchorUid != null)
        {
            var entryAnchor = Comp<NetAnchorComponent>(entryAnchorUid.Value);
            entryAnchor.Connected = true;
        }

        component.UsedLoad += module.RamCost;
        component.SpawnedModules.Add(loadedGridUid);
        Dirty(uid, component);

        _popup.PopupEntity($"{module.Name} пришит к порту.", uid, user);
        return true;
    }

    private Direction GetOppositeDirection(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East => Direction.West,
            Direction.West => Direction.East,
            _ => Direction.Invalid
        };
    }

    private string GetDeviceClass(EntityUid uid)
    {
        if (HasComp<DoorComponent>(uid))
            return "ДВЕРЬ";

        if (HasComp<SurveillanceCameraComponent>(uid))
            return "КАМЕРА";

        if (HasComp<PoweredLightComponent>(uid))
            return "СВЕТ";

        if (HasComp<DeviceNetworkComponent>(uid))
            return "УСТРОЙСТВО";

        if (HasComp<ApcPowerReceiverComponent>(uid))
            return "ПИТАНИЕ";

        return "НЕИЗВЕСТНО";
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
            _popup.PopupEntity("ОШИБКА СКАНА: сервер не видит прямой линии логического питания.", uid, PopupType.MediumCaution);
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

            if (HasComp<PoweredLightComponent>(device))
            {
                SpawnNodeForDevice(uid, component, hubGridUid, device, NetDeviceNodeKind.Generic, nodeCount++);
                continue;
            }

            if (HasComp<DeviceNetworkComponent>(device))
                SpawnNodeForDevice(uid, component, hubGridUid, device, NetDeviceNodeKind.Generic, nodeCount++);
        }

        if (cameraGroup.Count > 0)
            SpawnCameraGroupNode(uid, component, hubGridUid, cameraGroup, nodeCount++);

        if (nodeCount > 0)
        {
            _popup.PopupEntity($"СКАН ЗАВЕРШЕН: отображено узлов: {nodeCount}.", uid);
        }
        else
        {
            _popup.PopupEntity("СКАН ЗАВЕРШЕН: в этом сегменте ЛКП сетевые узлы не найдены.", uid);
        }
    }

    private HashSet<EntityUid> CollectNetworkDevices(EntityUid serverUid)
    {
        var devices = new HashSet<EntityUid>();

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
        var spawnTile = GetNodeSpawnTile(server, $"device:{device}", index);
        var coords = _mapSystem.GridTileToLocal(gridUid, Comp<MapGridComponent>(gridUid), spawnTile);
        var nodeUid = Spawn("NetDeviceNode", coords); 
        
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = device;
        nodeComp.Kind = kind;
        nodeComp.Server = serverUid;
        nodeComp.PhysicalDevices.Clear();
        nodeComp.PhysicalDevices.Add(device);

        RememberNodeLayout(server, nodeComp, spawnTile);
        
        _metaData.SetEntityName(nodeUid, $"Node: {Name(device)}");
        
        server.SpawnedNodes.Add(nodeUid);
    }

    private void SpawnCameraGroupNode(EntityUid serverUid, NetServerComponent server, EntityUid gridUid, List<EntityUid> cameras, int index)
    {
        var spawnTile = GetNodeSpawnTile(server, "camera_group", index);
        var coords = _mapSystem.GridTileToLocal(gridUid, Comp<MapGridComponent>(gridUid), spawnTile);
        var nodeUid = Spawn("NetDeviceNode", coords);
        var nodeComp = EnsureComp<NetDeviceNodeComponent>(nodeUid);
        nodeComp.PhysicalDevice = cameras[0];
        nodeComp.Kind = NetDeviceNodeKind.CameraGroup;
        nodeComp.Server = serverUid;
        nodeComp.PhysicalDevices.Clear();
        nodeComp.PhysicalDevices.AddRange(cameras);

        RememberNodeLayout(server, nodeComp, spawnTile);

        _metaData.SetEntityName(nodeUid, $"Node: Camera Control ({cameras.Count})");
        server.SpawnedNodes.Add(nodeUid);
    }
}
