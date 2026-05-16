using Content.Server.Mind;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckHotSimMessage>(OnHotSim);
        SubscribeLocalEvent<CyberdeckComponent, CyberdeckConstructMessage>(OnConstruct);
        SubscribeLocalEvent<NetAvatarComponent, JackOutActionEvent>(OnJackOut);
    }

    private void OnConstruct(EntityUid uid, CyberdeckComponent component, CyberdeckConstructMessage args)
    {
        var user = args.Actor;
        if (!user.Valid) return;

        if (!TryComp<NetNodeComponent>(uid, out var node) || node.DigitalGrid == null)
        {
            _popup.PopupEntity("ERROR: No active network link to construct.", uid, user, PopupType.MediumCaution);
            return;
        }

        if (!_proto.TryIndex<NetModulePrototype>(args.ModuleId, out var module)) return;

        // 1. RAM Check
        if (component.MaxRam - component.LeakedRam < module.RamCost)
        {
            _popup.PopupEntity($"Insufficient RAM capacity (Need {module.RamCost}).", uid, user, PopupType.MediumCaution);
            return;
        }

        var gridUid = node.DigitalGrid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid)) return;

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

            // Mark anchors as used
            targetAnchor.Connected = true;
            if (entryAnchorUid != null)
            {
                var entryAnchor = Comp<NetAnchorComponent>(entryAnchorUid.Value);
                entryAnchor.Connected = true;
            }

            // Permanent RAM deduction
            component.MaxRam -= module.RamCost;
            Dirty(uid, component);

            _popup.PopupEntity($"{module.Name} docked to port.", uid, user);

            // Refresh UI
            _metaProgram.UpdateUi(uid, component, user);
        }
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

        // 1. Resolve Anchor Server
        if (component.ActiveServer == null || Deleted(component.ActiveServer.Value))
        {
            _popup.PopupEntity("ERROR: Cyberdeck is not physically linked to a Net-Server.", uid, user, PopupType.MediumCaution);
            return;
        }

        var anchor = component.ActiveServer.Value;
        if (!TryComp<NetServerComponent>(anchor, out var server) || server.DigitalGrid == null)
        {
            _popup.PopupEntity("ERROR: Linked server hardware is offline or uninitialized.", anchor, user, PopupType.MediumCaution);
            return;
        }

        var netGrid = server.DigitalGrid.Value;

        // 2. Start Immersion Effect
        RaiseNetworkEvent(new NetrunningImmersionEvent(true), user);

        // 3. Wait for fade then transfer
        Timer.Spawn(TimeSpan.FromSeconds(1.6f), () => 
        {
            if (Deleted(user) || Deleted(uid)) return;

            // Freeze physical body without Stun
            EnsureComp<ImmersedBodyComponent>(user);

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
}
