using Content.Server.Hands.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._NC.RTS.Components;
using Content.Shared._NC.RTS.Events;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
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

        if (!HasComp<NPCRetaliationComponent>(ent.Owner))
            return;

        // Do not use vanilla friendly filtering here: RTS drones must answer
        // the entity that actually damaged them even in peaceful faction mode.
        _faction.AggroEntity(ent.Owner, attacker);

        if (!_hands.TryGetActiveItem(ent.Owner, out var heldItem) || !HasComp<GunComponent>(heldItem))
        {
            RetaliateWithDefaultNpcCombat(ent.Owner, attacker);
            return;
        }

        RetaliateWithRangedCombat(ent.Owner, attacker);
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
