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
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MetaProgramSystem _metaProgram = default!;

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
        SubscribeLocalEvent<NetServerComponent, GetVerbsEvent<ActivationVerb>>(OnServerVerbs);
        SubscribeLocalEvent<NetDeviceNodeComponent, ActivateInWorldEvent>(OnNodeActivate);
        SubscribeLocalEvent<NetDeviceNodeComponent, NetNodeControlMessage>(OnControlMessage);
        SubscribeLocalEvent<NetDeviceNodeComponent, NetNodeExecuteShardMessage>(OnExecuteShardMessage);
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

        // Ensure netrunner can "see" through the device
        var eyeComp = EnsureComp<EyeComponent>(physical);
        _eye.SetVisibilityMask(physical, 1, eyeComp); // Use int 1 for basic visibility

        _ui.OpenUi(uid, NetNodeUiKey.Key, args.User);
        UpdateNodeUi(uid, component, args.User);
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
            modules,
            anchors,
            devices);

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
