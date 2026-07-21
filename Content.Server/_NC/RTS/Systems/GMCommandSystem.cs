using System.Linq;
using Content.Server.Hands.Systems;
using Content.Server.Interaction;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._NC.RTS.Components;
using Content.Shared._NC.RTS.Events;
using Content.Shared.CombatMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Server._NC.RTS.Systems;

/// <summary>
/// Executes active RTS orders directly on the server and temporarily overrides
/// the NPC's HTN plan until the order is cleared.
/// </summary>
public sealed partial class GMCommandSystem : EntitySystem
{
    private const string ManualCommandKey = "InManualCommand";
    private const string TargetKey = "Target";
    private const string TargetCoordinatesKey = "TargetCoordinates";

    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RTSControllableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var rts, out var xform))
        {
            if (rts.ActiveCommand == null)
                continue;

            switch (rts.ActiveCommand)
            {
                case RTSCommandType.Move:
                    HandleMove(uid, rts, xform);
                    break;
                case RTSCommandType.AttackMove:
                    HandleAttackMove(uid, rts, xform);
                    break;
                case RTSCommandType.AttackTarget:
                    HandleAttackTarget(uid, rts);
                    break;
                case RTSCommandType.HoldPosition:
                    HandleHoldPosition(uid);
                    break;
                case RTSCommandType.Stop:
                    ClearCommand(uid, rts);
                    break;
            }
        }
    }

    private void HandleMove(EntityUid uid, RTSControllableComponent rts, TransformComponent xform)
    {
        if (rts.Destination == null)
        {
            ClearCommand(uid, rts);
            return;
        }

        if (xform.Coordinates.InRange(EntityManager, _transform, rts.Destination.Value, rts.ArrivalRange))
        {
            ClearCommand(uid, rts);
            return;
        }

        SuppressRangedCombat(uid);
        EnsureSteering(uid, rts.Destination.Value);
    }

    private void HandleAttackMove(EntityUid uid, RTSControllableComponent rts, TransformComponent xform)
    {
        if (rts.Destination == null)
        {
            ClearCommand(uid, rts);
            return;
        }

        if (xform.Coordinates.InRange(EntityManager, _transform, rts.Destination.Value, rts.ArrivalRange))
        {
            ClearCommand(uid, rts);
            return;
        }

        if (rts.TargetEntity is { } currentTarget)
        {
            if (CanEngageHostile(uid, rts, currentTarget))
            {
                ClearCommand(uid, rts);
                return;
            }

            rts.TargetEntity = null;
            Dirty(uid, rts);
            SuppressRangedCombat(uid);
        }

        if (TryGetNearestHostile(uid, rts, out var hostile))
        {
            ClearCommand(uid, rts);
            return;
        }

        SuppressRangedCombat(uid);
        EnsureSteering(uid, rts.Destination.Value);
    }

    private void HandleAttackTarget(EntityUid uid, RTSControllableComponent rts)
    {
        if (rts.TargetEntity == null ||
            !Exists(rts.TargetEntity.Value) ||
            !CanAttackMobState(uid, rts.TargetEntity.Value) ||
            _faction.NCIsRtsFriendly(uid, rts.TargetEntity.Value))
        {
            ClearCommand(uid, rts);
            return;
        }

        var targetXform = Transform(rts.TargetEntity.Value);
        var collisionGroup = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;
        if (_interaction.InRangeUnobstructed(uid, rts.TargetEntity.Value, rts.AttackRange, collisionGroup))
            _steering.Unregister(uid);
        else
            EnsureSteering(uid, targetXform.Coordinates, rts.AttackApproachRange);

        EngageTarget(uid, rts.TargetEntity.Value);
    }

    private void HandleHoldPosition(EntityUid uid)
    {
        _steering.Unregister(uid);
        SuppressRangedCombat(uid);
    }

    /// <summary>
    /// Keeps the pathing target synced with the RTS order without spamming
    /// reregistration when the destination did not materially change.
    /// </summary>
    private void EnsureSteering(EntityUid uid, EntityCoordinates target, float range = 0.2f)
    {
        if (!TryComp<NPCSteeringComponent>(uid, out var steering))
        {
            steering = _steering.Register(uid, target);
            steering.Range = range;
            return;
        }

        steering.Range = range;
        if (steering.Coordinates.TryDistance(EntityManager, target, out var dist) && dist < 0.5f)
            return;

        _steering.Unregister(uid);
        steering = _steering.Register(uid, target);
        steering.Range = range;
    }

    private bool TryGetNearestHostile(EntityUid uid, RTSControllableComponent rts, out EntityUid hostile)
    {
        hostile = EntityUid.Invalid;

        if (!_hands.TryGetActiveItem(uid, out var heldItem) || !HasComp<GunComponent>(heldItem))
            return false;

        hostile = _faction.GetNearbyHostiles(uid, rts.ScanRadius)
            .FirstOrDefault(h => CanEngageHostile(uid, rts, h));

        return hostile != EntityUid.Invalid && Exists(hostile);
    }

    private bool CanEngageHostile(EntityUid uid, RTSControllableComponent rts, EntityUid hostile)
    {
        if (hostile == EntityUid.Invalid || !Exists(hostile))
            return false;

        if (!CanAttackMobState(uid, hostile))
            return false;

        if (_faction.NCIsRtsFriendly(uid, hostile))
            return false;

        if (Transform(hostile).MapID != Transform(uid).MapID)
            return false;

        var collisionGroup = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;
        return _interaction.InRangeUnobstructed(uid, hostile, rts.ScanRadius, collisionGroup);
    }

    /// <summary>
    /// Normal drones stop at incapacitation while aggressive drones continue until death.
    /// </summary>
    private bool CanAttackMobState(EntityUid uid, EntityUid target)
    {
        if (!TryComp<MobStateComponent>(target, out var mobState))
            return true;

        return TryComp<RTSAggressionModeComponent>(uid, out var aggression) &&
               aggression.CurrentMode == RTSAggressionMode.Aggressive
            ? mobState.CurrentState < MobState.Dead
            : mobState.CurrentState <= MobState.Alive;
    }

    /// <summary>
    /// Reuses the engine's standard ranged NPC combat component instead of
    /// inventing a second combat path for RTS-controlled entities.
    /// </summary>
    private void EngageTarget(EntityUid uid, EntityUid target)
    {
        _combatMode.SetInCombatMode(uid, true);
        var combat = EnsureComp<NPCRangedCombatComponent>(uid);
        combat.Target = target;

        // Ranged NPC combat sets Unspecified when the gun runs dry.
        // A persistent RTS attack order must wake it back up after reload.
        if (combat.Status != CombatStatus.Unspecified)
            return;

        combat.Status = CombatStatus.Normal;
        combat.LOSAccumulator = 0f;
        combat.TargetInLOS = false;
    }

    public void StopCommand(EntityUid uid, RTSControllableComponent rts)
    {
        ClearCommand(uid, rts);
    }

    private void ClearCommand(EntityUid uid, RTSControllableComponent rts)
    {
        rts.ActiveCommand = null;
        rts.Destination = null;
        rts.TargetEntity = null;
        Dirty(uid, rts);

        _steering.Unregister(uid);
        SuppressRangedCombat(uid);

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.Remove<object>(TargetKey);
        htn.Blackboard.Remove<object>(TargetCoordinatesKey);
        htn.Blackboard.Remove<object>(ManualCommandKey);

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }

    /// <summary>
    /// Plain RTS movement must not allow stale ranged AI state to keep firing
    /// while the NPC is walking to the ordered point.
    /// </summary>
    private void SuppressRangedCombat(EntityUid uid)
    {
        _combatMode.SetInCombatMode(uid, false);
        RemComp<NPCRangedCombatComponent>(uid);

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            htn.Blackboard.Remove<object>(TargetKey);
            htn.Blackboard.Remove<object>(TargetCoordinatesKey);
        }

        if (TryComp<AutoShootGunComponent>(uid, out var ownerAutoShoot))
            _gun.SetEnabled(uid, ownerAutoShoot, false);

        if (!_hands.TryGetActiveItem(uid, out var heldItem))
            return;

        if (TryComp<AutoShootGunComponent>(heldItem, out var heldAutoShoot))
            _gun.SetEnabled(heldItem.Value, heldAutoShoot, false);

        if (!TryComp<GunComponent>(heldItem, out var gun))
            return;

        gun.ShootCoordinates = null;
        gun.Target = null;
        gun.ShotCounter = 0;
        gun.BurstActivated = false;
        gun.BurstShotsCount = 0;
        Dirty(heldItem.Value, gun);
    }
}
