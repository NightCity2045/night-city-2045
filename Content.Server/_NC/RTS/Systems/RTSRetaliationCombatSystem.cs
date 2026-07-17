using Content.Server.Hands.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._NC.Rigger.Components;
using Content.Shared._NC.RTS.Components;
using Content.Shared._NC.RTS.Events;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._NC.RTS.Systems;

/// <summary>
/// Keeps retaliation for RTS-controlled NPCs aligned with the weapon they are
/// actually holding instead of letting the generic hostile HTN fall into melee.
/// </summary>
public sealed class RTSRetaliationCombatSystem : EntitySystem
{
    private const string ManualCommandKey = "InManualCommand";
    private const string TargetKey = "Target";
    private const string TargetCoordinatesKey = "TargetCoordinates";

    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly NpcFactionSystem _faction = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RTSControllableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.SoftCritical))
            return;

        // State changes are rare, and matching the stored target reference avoids a per-frame world scan.
        var query = EntityQueryEnumerator<RTSControllableComponent, RTSAggressionModeComponent>();
        while (query.MoveNext(out var uid, out var rts, out var aggression))
        {
            if (aggression.CurrentMode != RTSAggressionMode.Aggressive ||
                IsFriendlyAttacker(uid, args.Target) ||
                !WasAttackingTarget(uid, rts, args.Target))
            {
                continue;
            }

            ContinueAttackUntilDead(uid, rts, args.Target);
        }
    }

    private bool WasAttackingTarget(EntityUid uid, RTSControllableComponent rts, EntityUid target)
    {
        if (rts.TargetEntity == target)
            return true;

        if (TryComp<NPCRangedCombatComponent>(uid, out var ranged) && ranged.Target == target)
            return true;

        return TryComp<HTNComponent>(uid, out var htn) &&
               htn.Blackboard.TryGetValue<EntityUid>(TargetKey, out var blackboardTarget, EntityManager) &&
               blackboardTarget == target;
    }

    private void ContinueAttackUntilDead(EntityUid uid, RTSControllableComponent rts, EntityUid target)
    {
        rts.Destination = null;
        rts.TargetEntity = target;
        rts.ActiveCommand = RTSCommandType.AttackTarget;
        Dirty(uid, rts);

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.SetValue(ManualCommandKey, true);
        htn.Blackboard.SetValue(TargetKey, target);
        htn.Blackboard.SetValue(TargetCoordinatesKey, Transform(target).Coordinates);

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);
    }

    private void OnDamageChanged(Entity<RTSControllableComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased ||
            args.Origin is not { } attacker ||
            !HasComp<NPCRetaliationComponent>(ent.Owner) ||
            !HasComp<MobStateComponent>(attacker))
        {
            return;
        }

        // Manual RTS orders own the NPC until they end. Plain Move explicitly
        // ignores aggression, and AttackTarget should not be retargeted by chip damage.
        if (ent.Comp.ActiveCommand != null)
            return;

        if (!HasComp<NPCRetaliationComponent>(ent.Owner) ||
            IsFriendlyAttacker(ent.Owner, attacker))
            return;

        _faction.AggroEntity(ent.Owner, attacker);

        if (!_hands.TryGetActiveItem(ent.Owner, out var heldItem) || !HasComp<GunComponent>(heldItem))
        {
            RetaliateWithDefaultNpcCombat(ent.Owner, attacker);
            return;
        }

        RetaliateWithRangedCombat(ent.Owner, attacker);
    }

    private bool IsFriendlyAttacker(EntityUid uid, EntityUid attacker)
    {
        // DroneFactions remain stable even when RTS peaceful mode temporarily
        // swaps the active NPC faction to Passive.
        if (TryComp<RiggerDroneComponent>(uid, out var drone) &&
            drone.DroneFactions.Count != 0 &&
            TryComp<NpcFactionMemberComponent>(attacker, out var attackerFaction) &&
            _faction.IsMemberOfAny((attacker, attackerFaction), drone.DroneFactions))
        {
            return true;
        }

        return _faction.IsEntityFriendly(uid, attacker);
    }

    private void RetaliateWithRangedCombat(EntityUid uid, EntityUid attacker)
    {
        _steering.Unregister(uid);
        RemComp<NPCMeleeCombatComponent>(uid);

        var ranged = EnsureComp<NPCRangedCombatComponent>(uid);
        ranged.Target = attacker;
        ranged.Status = CombatStatus.Normal;
        ranged.ShootAccumulator = 0f;
        ranged.LOSAccumulator = 0f;
        ranged.TargetInLOS = false;

        _combatMode.SetInCombatMode(uid, true);

        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.SetValue(TargetKey, attacker);
        htn.Blackboard.SetValue(TargetCoordinatesKey, Transform(attacker).Coordinates);

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }

    private void RetaliateWithDefaultNpcCombat(EntityUid uid, EntityUid attacker)
    {
        // Fall back to the vanilla faction exception path for non-ranged NPCs.
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.SetValue(TargetKey, attacker);
        htn.Blackboard.SetValue(TargetCoordinatesKey, Transform(attacker).Coordinates);

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }
}
