using Content.Server.Mind;
using Content.Server.Power.Components;
using Content.Shared._NC.Power.Components;
using Content.Shared._NC.Netrunning;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Prototypes;
using Content.Shared.Actions;
using Content.Shared.Stunnable;
using Content.Shared.Gravity;
using Content.Server.Atmos.Components;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;
using System.Linq;
using System.Numerics;

using Content.Shared.StatusEffect;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class HotSimSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly NetSpatialSystem _netSpatial = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaProgramSystem _metaProgram = default!;
    [Dependency] private readonly NetServerSystem _netServer = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckHotSimMessage>(OnHotSim);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckConstructMessage>(OnConstruct);
        SubscribeLocalEvent<NetAvatarComponent, JackOutActionEvent>(OnJackOut);
        SubscribeLocalEvent<NetModuleComponent, ComponentShutdown>(OnModuleShutdown);
    }

    private void OnConstruct(EntityUid uid, CyberdeckComponent component, CyberdeckConstructMessage args)
    {
        var user = args.Actor;
        if (!user.Valid) return;

        if (component.ActiveServer is not { } serverUid ||
            Deleted(serverUid) ||
            !TryComp<NetServerComponent>(serverUid, out var server) ||
            server.DigitalGrid == null)
        {
            _popup.PopupEntity("ERROR: No active network link to construct.", uid, user, PopupType.MediumCaution);
            return;
        }

        if (!_proto.TryIndex<NetModulePrototype>(args.ModuleId, out var module)) return;

        if (!HasServerAdminAccess(component, serverUid))
        {
            _popup.PopupEntity("ERROR: Root/admin access required for NET construction.", uid, user, PopupType.MediumCaution);
            return;
        }

        if (server.SpawnedModules.Count >= server.MaxModules)
        {
            _popup.PopupEntity($"ERROR: Server module limit reached ({server.MaxModules}).", uid, user, PopupType.MediumCaution);
            return;
        }

        // Persistent rooms reserve physical server load, not cyberdeck RAM.
        if (server.UsedLoad + module.RamCost > server.MaxLoad)
        {
            _popup.PopupEntity($"ERROR: Server load exceeded ({server.UsedLoad + module.RamCost}/{server.MaxLoad}).", uid, user, PopupType.MediumCaution);
            return;
        }

        var gridUid = server.DigitalGrid.Value;
        if (!HasComp<MapGridComponent>(gridUid)) return;

        // 2. Resolve the selected anchor
        var targetAnchorUid = GetEntity(args.Anchor);
        if (!TryComp<NetAnchorComponent>(targetAnchorUid, out var targetAnchor) || targetAnchor.Connected)
        {
            _popup.PopupEntity("ERROR: Expansion port is invalid or occupied.", uid, user, PopupType.MediumCaution);
            return;
        }

        // 3. Load the new module
        var targetXform = Transform(targetAnchorUid);
        if (_mapLoader.TryLoadGrid(targetXform.MapID, new ResPath(module.TemplatePath), out var newModuleGrid))
        {
            var loadedGridUid = newModuleGrid.Value.Owner;
            
            // 4. Find the matching entry anchor on the NEW grid
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

            Vector2 entryRelativePos = Vector2.Zero;
            if (entryAnchorUid != null)
            {
                entryRelativePos = Transform(entryAnchorUid.Value).LocalPosition;
            }

            // 5. Calculate docking position (FLUSH)
            var targetWorldPos = targetXform.WorldPosition;
            var spawnWorldPos = targetWorldPos - entryRelativePos;

            Transform(loadedGridUid).WorldPosition = spawnWorldPos;
            
            // Link metadata
            var modComp = EnsureComp<NetModuleComponent>(loadedGridUid);
            modComp.PrototypeId = module.ID;
            modComp.ReservedLoad = module.RamCost;
            modComp.Server = serverUid;

            // Mark anchors as used
            targetAnchor.Connected = true;
            if (entryAnchorUid != null)
            {
                var entryAnchor = Comp<NetAnchorComponent>(entryAnchorUid.Value);
                entryAnchor.Connected = true;
            }

            server.UsedLoad += module.RamCost;
            server.SpawnedModules.Add(loadedGridUid);
            Dirty(serverUid, server);

            _popup.PopupEntity($"{module.Name} docked to port.", uid, user);

            // Refresh UI
            _metaProgram.UpdateUi(uid, component, user);
        }
    }

    private bool HasServerAdminAccess(CyberdeckComponent deck, EntityUid serverUid)
    {
        // Direct server-console link represents local sysadmin maintenance access.
        return deck.ActiveTarget == serverUid || deck.HackedNetworks.Contains(serverUid);
    }

    private void OnModuleShutdown(EntityUid uid, NetModuleComponent component, ComponentShutdown args)
    {
        if (component.Server is not { } serverUid ||
            Deleted(serverUid) ||
            !TryComp<NetServerComponent>(serverUid, out var server))
            return;

        server.SpawnedModules.Remove(uid);
        server.UsedLoad = Math.Max(0, server.UsedLoad - component.ReservedLoad);
        Dirty(serverUid, server);
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

    private void OnHotSim(EntityUid uid, CyberdeckComponent component, CyberdeckHotSimMessage args)
    {
        var user = args.Actor;
        if (!user.Valid)
            return;

        if (!_mindSystem.TryGetMind(user, out var mindId, out var mind))
            return;

        // 1. Resolve the local network server. A deck can enter through the
        // server itself or through any electronics connected to the same LCP/APC.
        if (!TryResolveAnchorServer(component, out var anchor, out var server))
        {
            _popup.PopupEntity("ERROR: No local network server found for this link.", uid, user, PopupType.MediumCaution);
            return;
        }

        component.ActiveServer = anchor;
        Dirty(uid, component);

        _netServer.RefreshNetwork(anchor, server);

        if (server.DigitalGrid == null)
        {
            _popup.PopupEntity("ERROR: Server failed to initialize digital space.", anchor, user, PopupType.MediumCaution);
            return;
        }

        var netGrid = server.DigitalGrid.Value;
        var mapId = Transform(netGrid).MapID;

        // FORCE UNPAUSE before mind transfer
        _mapSystem.SetPaused(mapId, false);

        // RE-UNPAUSE after a short delay to override any automatic engine pausing
        Timer.Spawn(TimeSpan.FromMilliseconds(500), () => 
        {
            if (_mapManager.MapExists(mapId))
                _mapSystem.SetPaused(mapId, false);
        });

        // 2. Start Immersion Effect
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), user);

        // 3. Wait for fade then transfer
        Timer.Spawn(TimeSpan.FromSeconds(1.6f), () => 
        {
            if (Deleted(user) || Deleted(uid)) return;

            // Freeze physical body without Stun
            EnsureComp<ImmersedBodyComponent>(user);

            // Double check unpause just in case engine re-paused during the timer
            _mapSystem.SetPaused(mapId, false);

            // Spawn Avatar at grid center
            var coords = new EntityCoordinates(netGrid, 0, 0);
            var avatar = Spawn("NCNetAvatar", coords);
            
            var avatarComp = EnsureComp<NetAvatarComponent>(avatar);
            avatarComp.PhysicalBody = user;
            avatarComp.Cyberdeck = uid;

            // Transfer Mind
            _mindSystem.Visit(mindId, avatar, mind);

            // Add Jack Out Action
            _actions.AddAction(avatar, "ActionNetJackOut");

            // 5. Open eyes
            RaiseNetworkEvent(new NetrunningImmersionEvent(false), avatar);

            // Refresh UI
            _metaProgram.UpdateUi(uid, component, user);
        });
    }

    private void OnJackOut(EntityUid uid, NetAvatarComponent component, JackOutActionEvent args)
    {
        if (!_mindSystem.TryGetMind(uid, out var mindId, out var mind))
            return;

        if (component.PhysicalBody is not { } body)
            return;

        // 1. Start Fade Effect
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), uid);

        // 2. Wait for fade then return
        Timer.Spawn(TimeSpan.FromSeconds(0.8f), () => 
        {
            if (Deleted(body)) return;

            // Return to body
            _mindSystem.UnVisit(mindId, mind);

            // 3. NUCLEAR CLEANUP
            RemComp<ImmersedBodyComponent>(body);
            
            // 4. Cleanup Avatar
            QueueDel(uid);

            // 5. Open eyes
            RaiseNetworkEvent(new NetrunningImmersionEvent(false), body);
        });
    }

    private bool TryResolveAnchorServer(CyberdeckComponent deck, out EntityUid anchor, out NetServerComponent server)
    {
        if (deck.ActiveServer is { } direct && !Deleted(direct) && TryComp<NetServerComponent>(direct, out var directServer))
        {
            anchor = direct;
            server = directServer;
            return true;
        }

        if (deck.ActiveTarget is not { } target || Deleted(target))
        {
            anchor = EntityUid.Invalid;
            server = default!;
            return false;
        }

        if (TryComp<NetServerComponent>(target, out var targetServer))
        {
            anchor = target;
            server = targetServer;
            return true;
        }

        var query = EntityQueryEnumerator<NetServerComponent>();
        while (query.MoveNext(out var serverUid, out var candidate))
        {
            if (SharesNetwork(target, serverUid))
            {
                anchor = serverUid;
                server = candidate;
                return true;
            }
        }

        anchor = EntityUid.Invalid;
        server = default!;
        return false;
    }

    private bool SharesNetwork(EntityUid deviceUid, EntityUid serverUid)
    {
        if (TryComp<ApcPowerReceiverComponent>(deviceUid, out var deviceApc) &&
            TryComp<ApcPowerReceiverComponent>(serverUid, out var serverApc) &&
            deviceApc.NetworkLoad.LinkedNetwork != default &&
            deviceApc.NetworkLoad.LinkedNetwork == serverApc.NetworkLoad.LinkedNetwork)
        {
            return true;
        }

        if (TryComp<LogicPowerReceiverComponent>(deviceUid, out var deviceLogic) && deviceLogic.Provider != null)
        {
            if (TryComp<LogicPowerReceiverComponent>(serverUid, out var serverLogic) &&
                serverLogic.Provider == deviceLogic.Provider)
            {
                return true;
            }

            if (serverUid == deviceLogic.Provider)
                return true;
        }

        if (TryComp<LogicPowerProviderComponent>(serverUid, out var serverProvider) &&
            serverProvider.Receivers.Contains(deviceUid))
        {
            return true;
        }

        return false;
    }
}
