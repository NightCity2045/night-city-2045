using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Administration;
using Content.Shared.CombatMode;
using Content.Shared._NC.Rigger.Components;
using Content.Shared._NC.RTS.Components;
using Content.Shared._NC.RTS.Events;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;

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
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RTSCommandEvent>(OnCommandReceived);
        SubscribeLocalEvent<RTSAggressionModeComponent, ComponentStartup>(OnAggressionStartup);
    }

    private void OnAggressionStartup(Entity<RTSAggressionModeComponent> ent, ref ComponentStartup args)
    {
        ApplyAggressionMode(ent.Owner, ent.Comp.CurrentMode);
    }

    private void OnCommandReceived(RTSCommandEvent ev, EntitySessionEventArgs args)
    {
        var isAdmin = _adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin);
        var rigger = GetRiggerSession(args.SenderSession);
        if (!isAdmin && rigger == null)
            return;

        foreach (var netEntity in ev.SelectedNpcs)
        {
            var uid = GetEntity(netEntity);

            if (!Exists(uid) ||
                !TryComp<RTSControllableComponent>(uid, out var rts) ||
                rigger != null && !rigger.Value.Comp.LinkedDrones.Contains(uid))
            {
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
                    var coords = ResolveTargetCoordinates(uid, ev);
                    if (coords == null)
                        continue;

                    rts.Destination = coords;
                    rts.ActiveCommand = ev.CommandType;
                    break;
                }

                case RTSCommandType.AttackTarget:
                {
                    if (ev.TargetEntity == null)
                        break;

                    var targetUid = GetEntity(ev.TargetEntity.Value);
                    if (!Exists(targetUid))
                        break;

                    rts.TargetEntity = targetUid;
                    rts.ActiveCommand = RTSCommandType.AttackTarget;
                    break;
                }

                case RTSCommandType.HoldPosition:
                    rts.ActiveCommand = RTSCommandType.HoldPosition;
                    break;

                case RTSCommandType.Stop:
                    _steering.Unregister(uid);
                    break;

                case RTSCommandType.SetPeacefulMode:
                    SetAggressionMode(uid, RTSAggressionMode.Peaceful);
                    break;

                case RTSCommandType.SetNormalMode:
                    SetAggressionMode(uid, RTSAggressionMode.Normal);
                    break;
            }

            Dirty(uid, rts);

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
    }

    private Entity<RiggerConsoleUserComponent>? GetRiggerSession(ICommonSession session)
    {
        var attached = session.AttachedEntity;
        if (attached == null ||
            !TryComp<RiggerConsoleUserComponent>(attached.Value, out var rigger) ||
            !rigger.RtsEnabled)
        {
            return null;
        }

        return (attached.Value, rigger);
    }

    private void SetAggressionMode(EntityUid uid, RTSAggressionMode mode)
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
    /// Applies peaceful/normal RTS mode through normal NPC factions so HTN
    /// continues to own target selection after manual commands end.
    /// </summary>
    private void ApplyAggressionMode(EntityUid uid, RTSAggressionMode mode)
    {
        if (!TryComp<RTSAggressionModeComponent>(uid, out var aggression))
            return;

        var targetFactions = mode == RTSAggressionMode.Peaceful
            ? aggression.PeacefulFactions
            : aggression.NormalFactions;

        var faction = EnsureComp<NpcFactionMemberComponent>(uid);
        _faction.ClearFactions((uid, faction), dirty: false);
        _faction.AddFactions((uid, faction), targetFactions, dirty: true);
        Dirty(uid, faction);

        ClearExceptionHostiles(uid);
        ClearCombatState(uid);

        if (TryComp<RTSControllableComponent>(uid, out var rts) && rts.ActiveCommand != null)
            return;

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
    private EntityCoordinates? ResolveTargetCoordinates(EntityUid uid, RTSCommandEvent ev)
    {
        if (ev.TargetEntity != null)
        {
            var targetUid = GetEntity(ev.TargetEntity.Value);
            if (Exists(targetUid))
                return Transform(targetUid).Coordinates;
        }

        if (ev.TargetPosition == null)
            return null;

        var xform = Transform(uid);
        var parentXform = Transform(xform.ParentUid);
        var localPos = Vector2.Transform(ev.TargetPosition.Value, _transform.GetInvWorldMatrix(parentXform));
        return new EntityCoordinates(xform.ParentUid, localPos);
    }
}
