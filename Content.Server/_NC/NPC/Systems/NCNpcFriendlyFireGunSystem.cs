using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NC.NPC.Systems;

/// <summary>
/// Server-side final guard for NPC gunfire. The regular NPC combat system can decide to shoot,
/// but this system cancels the actual gun attempt if a friendly faction member is on the shot ray.
/// </summary>
public sealed class NCNpcFriendlyFireGunSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _faction = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<GunComponent> gun, ref AttemptShootEvent args)
    {
        if (!TryComp<NPCRangedCombatComponent>(args.User, out var ranged) ||
            !ranged.NCAvoidFriendlyFire ||
            ranged.NCFriendlyFireLineRadius <= 0f ||
            !TryComp<NpcFactionMemberComponent>(args.User, out var shooterFaction))
        {
            return;
        }

        if (gun.Comp.ShootCoordinates == null)
            return;

        var from = _transform.GetMapCoordinates(args.User);
        var to = _transform.ToMapCoordinates(gun.Comp.ShootCoordinates.Value);

        if (from.MapId != to.MapId)
            return;

        if (!HasFriendlyOnShotRay(args.User, gun.Owner, ranged.Target, shooterFaction, from, to))
            return;

        args.Cancelled = true;
        ranged.ShootAccumulator = 0f;
        ranged.LOSAccumulator = 0f;
        Reposition(args.User, ranged, from, to);

        // Burst and autoshoot can keep calling AttemptShoot after the first blocked shot.
        gun.Comp.BurstActivated = false;
        gun.Comp.BurstShotsCount = 0;
        gun.Comp.ShotCounter = 0;
        gun.Comp.Target = null;

        if (TryComp<AutoShootGunComponent>(gun.Owner, out var gunAutoShoot))
            _gun.SetEnabled(gun.Owner, gunAutoShoot, false);

        if (TryComp<AutoShootGunComponent>(args.User, out var ownerAutoShoot))
            _gun.SetEnabled(args.User, ownerAutoShoot, false);

        _combat.SetInCombatMode(args.User, true);
        Dirty(gun);
    }

    private bool HasFriendlyOnShotRay(
        EntityUid shooter,
        EntityUid gun,
        EntityUid target,
        NpcFactionMemberComponent shooterFaction,
        MapCoordinates from,
        MapCoordinates to)
    {
        var shotVector = to.Position - from.Position;
        var shotLength = shotVector.Length();
        if (shotLength <= 0f)
            return false;

        var ray = new CollisionRay(from.Position, shotVector / shotLength, (int) CollisionGroup.BulletImpassable);
        var hits = _physics.IntersectRay(from.MapId, ray, shotLength, shooter, returnOnFirstHit: false)
            .OrderBy(hit => hit.Distance);

        foreach (var hit in hits)
        {
            var entity = hit.HitEntity;

            if (entity == shooter || entity == gun || Deleted(entity))
                continue;

            if (entity == target && !IsFriendly(shooter, shooterFaction, entity))
                return false;

            if (IsLiveFriendly(shooter, shooterFaction, entity))
                return true;

            // A non-friendly bullet blocker before any friendly means the shot cannot hit that friendly.
            return false;
        }

        return false;
    }

    private bool IsLiveFriendly(EntityUid shooter, NpcFactionMemberComponent shooterFaction, EntityUid other)
    {
        if (TryComp<MobStateComponent>(other, out var mobState) && mobState.CurrentState > MobState.Alive)
            return false;

        return IsFriendly(shooter, shooterFaction, other);
    }

    private bool IsFriendly(EntityUid shooter, NpcFactionMemberComponent shooterFaction, EntityUid other)
    {
        return TryComp<NpcFactionMemberComponent>(other, out var otherFaction) &&
               _faction.IsEntityFriendly((shooter, shooterFaction), (other, otherFaction));
    }

    private void Reposition(EntityUid shooter, NPCRangedCombatComponent ranged, MapCoordinates from, MapCoordinates to)
    {
        if (_timing.CurTime < ranged.NCFriendlyFireNextReposition)
            return;

        ranged.NCFriendlyFireNextReposition = _timing.CurTime + TimeSpan.FromSeconds(ranged.NCFriendlyFireRepositionCooldown);

        var shotVector = to.Position - from.Position;
        if (shotVector.LengthSquared() <= 0.001f)
            return;

        var direction = shotVector.Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var side = _random.Prob(0.5f) ? 1f : -1f;
        var desiredWorld = from.Position
                           + perpendicular * side * ranged.NCFriendlyFireRepositionDistance
                           - direction * ranged.NCFriendlyFireRepositionBackoff;

        var xform = Transform(shooter);
        var parentXform = Transform(xform.ParentUid);
        var desiredLocal = Vector2.Transform(desiredWorld, _transform.GetInvWorldMatrix(parentXform));
        var destination = new EntityCoordinates(xform.ParentUid, desiredLocal);

        var steering = _steering.Register(shooter, destination);
        steering.Range = ranged.NCFriendlyFireRepositionRange;
        steering.ForceMove = true;
    }
}
