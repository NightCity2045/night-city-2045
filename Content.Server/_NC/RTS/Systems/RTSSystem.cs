using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Administration;
using Content.Shared.CombatMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared._NC.Rigger.Components;
using Content.Shared._NC.Rigger;
using Content.Shared._NC.RTS.Components;
using Content.Shared._NC.RTS.Events;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.RTS.Systems;

/// <summary>
/// Accepts RTS commands from admin clients and writes them into replicated
/// component state so the server-side command executor can take over the NPC.
/// </summary>
public sealed partial class RTSSystem : EntitySystem
{
    private const string ManualCommandKey = "InManualCommand";
    private const string TargetKey = "Target";
    private const string TargetCoordinatesKey = "TargetCoordinates";

    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private GMCommandSystem _gmCommands = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private RiggerVisionSystem _riggerVision = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RTSCommandEvent>(OnCommandReceived);
        SubscribeLocalEvent<RTSAggressionModeComponent, ComponentStartup>(OnAggressionStartup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnAggressionStartup(Entity<RTSAggressionModeComponent> ent, ref ComponentStartup args)
    {
        ApplyAggressionMode(ent.Owner, ent.Comp.CurrentMode);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<NpcFactionPrototype>())
            return;

        var query = EntityQueryEnumerator<RTSAggressionModeComponent>();
        while (query.MoveNext(out var uid, out var aggression))
        {
            if (aggression.CurrentMode == RTSAggressionMode.Aggressive)
                ApplyAggressionMode(uid, aggression.CurrentMode);
        }
    }

    private void OnCommandReceived(RTSCommandEvent ev, EntitySessionEventArgs args)
    {
        var isAdmin = _adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin);
        var rigger = GetRiggerSession(args.SenderSession);
        if (!isAdmin && rigger == null)
            return;

        var accepted = 0;
        var rejected = 0;

        for (var formationIndex = 0; formationIndex < ev.SelectedNpcs.Count; formationIndex++)
        {
            var netEntity = ev.SelectedNpcs[formationIndex];
            var uid = GetEntity(netEntity);

            if (!CanReceiveCommand(uid, isAdmin, rigger, out var rts))
            {
                rejected++;
                continue;
            }

            if (rigger != null && !CanRiggerCommandLocation(rigger.Value.Owner, uid, ev))
            {
                rejected++;
                continue;
            }

            // Aggression is an autonomous-behaviour setting, not a manual order.
            // Changing it must not erase an existing move, hold, or attack command.
            if (TryHandleAggressionCommand(uid, ev.CommandType))
            {
                accepted++;
                continue;
            }

            rts.Destination = null;
            rts.TargetEntity = null;
            rts.ActiveCommand = null;

            switch (ev.CommandType)
            {
                case RTSCommandType.Move:
                case RTSCommandType.AttackMove:
                {
                    var coords = ResolveTargetCoordinates(
                        uid,
                        ev,
                        GetFormationOffset(formationIndex, ev.SelectedNpcs.Count, rts.FormationSpacing));
                    if (coords == null)
                    {
                        rejected++;
                        continue;
                    }

                    rts.Destination = coords;
                    rts.ActiveCommand = ev.CommandType;
                    break;
                }

                case RTSCommandType.AttackTarget:
                {
                    if (ev.TargetEntity == null)
                    {
                        rejected++;
                        continue;
                    }

                    var targetUid = GetEntity(ev.TargetEntity.Value);
                    if (!IsValidTarget(uid, targetUid) ||
                        IsFriendlyTarget(uid, targetUid) ||
                        rigger != null && !CanRiggerAccessTarget(rigger.Value.Owner, uid, targetUid))
                    {
                        rejected++;
                        continue;
                    }

                    rts.TargetEntity = targetUid;
                    rts.ActiveCommand = RTSCommandType.AttackTarget;
                    break;
                }

                case RTSCommandType.HoldPosition:
                    rts.ActiveCommand = RTSCommandType.HoldPosition;
                    break;

                case RTSCommandType.Stop:
                    _gmCommands.StopCommand(uid, rts);
                    accepted++;
                    continue;

            }

            Dirty(uid, rts);
            accepted++;

            if (!TryComp<HTNComponent>(uid, out var htn))
                continue;

            if (rts.ActiveCommand != null)
                htn.Blackboard.SetValue(ManualCommandKey, true);
            else
                htn.Blackboard.Remove<object>(ManualCommandKey);

            // Shut the running plan down immediately so direct RTS execution owns the NPC.
            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            if (rts.ActiveCommand == null)
                _htn.Replan(htn);
        }

        PopupCommandResult(args.SenderSession, accepted, rejected);
    }

    private bool CanReceiveCommand(
        EntityUid uid,
        bool isAdmin,
        RiggerCommandSession? rigger,
        out RTSControllableComponent rts)
    {
        rts = null!;

        if (!Exists(uid) || !IsAlive(uid))
            return false;

        if (rigger != null)
        {
            if (!TryComp<RTSControllableComponent>(uid, out var existingRts) ||
                !rigger.Value.LinkedDrones.Contains(uid))
            {
                return false;
            }

            rts = existingRts;
            return true;
        }

        if (!isAdmin || !IsNCAdminCommandableMob(uid))
            return false;

        rts = EnsureComp<RTSControllableComponent>(uid);
        return true;
    }

    private bool IsNCAdminCommandableMob(EntityUid uid)
    {
        if (!HasComp<MobStateComponent>(uid) ||
            !HasComp<NpcFactionMemberComponent>(uid))
        {
            return false;
        }

        var prototypeId = MetaData(uid).EntityPrototype?.ID;
        return prototypeId != null && prototypeId.StartsWith("MobNC", StringComparison.Ordinal);
    }

    private bool IsAlive(EntityUid uid)
    {
        return !TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState <= MobState.Alive;
    }

    private bool IsValidTarget(EntityUid controlled, EntityUid target)
    {
        if (!Exists(target) || !TryComp<MobStateComponent>(target, out var mobState))
            return Exists(target);

        return TryComp<RTSAggressionModeComponent>(controlled, out var aggression) &&
               aggression.CurrentMode == RTSAggressionMode.Aggressive
            ? mobState.CurrentState < MobState.Dead
            : mobState.CurrentState <= MobState.Alive;
    }

    private bool IsFriendlyTarget(EntityUid controlled, EntityUid target)
    {
        if (TryComp<RiggerDroneComponent>(controlled, out var drone) &&
            TryComp<NpcFactionMemberComponent>(target, out var targetFaction) &&
            _faction.IsMemberOfAny((target, targetFaction), drone.DroneFactions))
        {
            return true;
        }

        return _faction.IsEntityFriendly(controlled, target);
    }

    private bool CanRiggerCommandLocation(EntityUid riggerEye, EntityUid controlled, RTSCommandEvent ev)
    {
        if (ev.CommandType is RTSCommandType.HoldPosition
            or RTSCommandType.Stop
            or RTSCommandType.SetPeacefulMode
            or RTSCommandType.SetNormalMode
            or RTSCommandType.SetAggressiveMode)
        {
            return true;
        }

        if (ev.TargetEntity != null)
            return CanRiggerAccessTarget(riggerEye, controlled, GetEntity(ev.TargetEntity.Value));

        if (ev.TargetPosition == null)
            return false;

        return CanRiggerAccessMapPosition(riggerEye, controlled, ev.TargetPosition.Value);
    }

    private bool CanRiggerAccessTarget(EntityUid riggerEye, EntityUid controlled, EntityUid target)
    {
        if (!Exists(target))
            return false;

        return CanRiggerAccessMapCoordinates(riggerEye, _transform.GetMapCoordinates(target));
    }

    private bool CanRiggerAccessMapPosition(EntityUid riggerEye, EntityUid controlled, Vector2 position)
    {
        var mapId = Transform(controlled).MapID;
        return CanRiggerAccessMapCoordinates(riggerEye, new MapCoordinates(position, mapId));
    }

    private bool CanRiggerAccessMapCoordinates(EntityUid riggerEye, MapCoordinates coordinates)
    {
        if (!_mapManager.TryFindGridAt(coordinates, out var gridUid, out var grid) ||
            !TryComp<BroadphaseComponent>(gridUid, out var broadphase))
        {
            return false;
        }

        var tile = _map.GetTileRef(gridUid, grid, coordinates);
        return _riggerVision.IsAccessible(riggerEye, (gridUid, broadphase, grid), tile.GridIndices);
    }

    private void PopupCommandResult(ICommonSession session, int accepted, int rejected)
    {
        var attached = session.AttachedEntity;
        if (attached == null)
            return;

        if (accepted > 0)
        {
            _popup.PopupClient(Loc.GetString("nc-rts-command-accepted", ("count", accepted)), attached.Value, attached.Value);
            return;
        }

        if (rejected > 0)
            _popup.PopupClient(Loc.GetString("nc-rts-command-rejected"), attached.Value, attached.Value);
    }

    private RiggerCommandSession? GetRiggerSession(ICommonSession session)
    {
        var attached = session.AttachedEntity;
        if (attached == null)
            return null;

        if (TryComp<RiggerConsoleUserComponent>(attached.Value, out var consoleRigger) &&
            consoleRigger.RtsEnabled)
            return new RiggerCommandSession(attached.Value, consoleRigger.LinkedDrones);

        if (TryComp<RiggerLaptopUserComponent>(attached.Value, out var laptopRigger) &&
            laptopRigger.RtsEnabled)
            return new RiggerCommandSession(attached.Value, laptopRigger.LinkedDrones);

        return null;
    }

    private readonly record struct RiggerCommandSession(EntityUid Owner, List<EntityUid> LinkedDrones);

    private bool TryHandleAggressionCommand(EntityUid uid, RTSCommandType command)
    {
        var mode = command switch
        {
            RTSCommandType.SetPeacefulMode => RTSAggressionMode.Peaceful,
            RTSCommandType.SetNormalMode => RTSAggressionMode.Normal,
            RTSCommandType.SetAggressiveMode => RTSAggressionMode.Aggressive,
            _ => (RTSAggressionMode?) null
        };

        if (mode == null)
            return false;

        SetAggressionMode(uid, mode.Value);
        return true;
    }

    public void SetAggressionMode(EntityUid uid, RTSAggressionMode mode)
    {
        if (!TryComp<RTSAggressionModeComponent>(uid, out var aggression))
            return;

        if (aggression.CurrentMode != mode)
        {
            aggression.CurrentMode = mode;
            Dirty(uid, aggression);
        }

        ApplyAggressionMode(uid, mode);
    }

    /// <summary>
    /// Applies RTS aggression through normal NPC factions so HTN continues to
    /// own target selection after manual commands end.
    /// </summary>
    private void ApplyAggressionMode(EntityUid uid, RTSAggressionMode mode)
    {
        if (!TryComp<RTSAggressionModeComponent>(uid, out var aggression))
            return;

        var targetFactions = mode switch
        {
            RTSAggressionMode.Peaceful => aggression.PeacefulFactions,
            RTSAggressionMode.Normal => aggression.NormalFactions,
            RTSAggressionMode.Aggressive => aggression.NormalFactions,
            _ => aggression.NormalFactions
        };

        var faction = EnsureComp<NpcFactionMemberComponent>(uid);
        _faction.ClearFactions((uid, faction), dirty: false);
        _faction.AddFactions((uid, faction), targetFactions, dirty: true);

        if (mode == RTSAggressionMode.Aggressive)
            _faction.NCApplyAggressiveRtsHostiles((uid, faction), aggression.PeacefulFactions);

        Dirty(uid, faction);

        ClearExceptionHostiles(uid);

        // Manual RTS execution owns steering and combat until its order completes.
        if (TryComp<RTSControllableComponent>(uid, out var rts) && rts.ActiveCommand != null)
            return;

        ClearCombatState(uid);

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }

    private void ClearExceptionHostiles(EntityUid uid)
    {
        if (!TryComp<FactionExceptionComponent>(uid, out var exceptions))
            return;

        foreach (var hostile in exceptions.Hostiles.ToArray())
        {
            _faction.DeAggroEntity((uid, exceptions), hostile);
        }

        Dirty(uid, exceptions);
    }

    private void ClearCombatState(EntityUid uid)
    {
        _combatMode.SetInCombatMode(uid, false);
        RemComp<NPCRangedCombatComponent>(uid);
        RemComp<NPCMeleeCombatComponent>(uid);

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.Remove<object>(TargetKey);
        htn.Blackboard.Remove<object>(TargetCoordinatesKey);
    }

    /// <summary>
    /// Resolves click target data into coordinates in the controlled NPC's parent space.
    /// </summary>
    private EntityCoordinates? ResolveTargetCoordinates(EntityUid uid, RTSCommandEvent ev, Vector2 formationOffset)
    {
        Vector2 targetPosition;

        if (ev.TargetEntity != null)
        {
            var targetUid = GetEntity(ev.TargetEntity.Value);
            if (!Exists(targetUid))
                return null;

            targetPosition = _transform.GetMapCoordinates(targetUid).Position;
        }
        else
        {
            if (ev.TargetPosition == null)
                return null;

            targetPosition = ev.TargetPosition.Value;
        }

        var xform = Transform(uid);
        var parentXform = Transform(xform.ParentUid);
        var localPos = Vector2.Transform(targetPosition + formationOffset, _transform.GetInvWorldMatrix(parentXform));
        return new EntityCoordinates(xform.ParentUid, localPos);
    }

    /// <summary>
    /// Places group destinations on a centered square grid so units do not compete for one point.
    /// </summary>
    private static Vector2 GetFormationOffset(int index, int count, float spacing)
    {
        if (count <= 1 || spacing <= 0f)
            return Vector2.Zero;

        var columns = (int) MathF.Ceiling(MathF.Sqrt(count));
        var rows = (int) MathF.Ceiling((float) count / columns);
        var column = index % columns;
        var row = index / columns;

        return new Vector2(
            (column - (columns - 1) * 0.5f) * spacing,
            (row - (rows - 1) * 0.5f) * spacing);
    }
}
