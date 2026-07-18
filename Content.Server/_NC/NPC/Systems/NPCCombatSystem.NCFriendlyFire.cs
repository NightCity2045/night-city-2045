using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared._NC.Rigger.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly EntityLookupSystem _ncLookup = default!;
    [Dependency] private readonly NpcFactionSystem _ncFaction = default!;

    /// <summary>
    /// Checks a narrow shot corridor for friendly faction members before an NPC pulls the trigger.
    /// This stays in the NPC combat path, so player-controlled gunfire is not affected.
    /// </summary>
    private bool NCIsFriendlyInLineOfFire(
        EntityUid shooter,
        EntityUid target,
        NPCRangedCombatComponent ranged,
        TransformComponent shooterXform,
        Vector2 shooterWorldPos,
        Vector2 targetWorldPos,
        out EntityUid blockingFriendly)
    {
        blockingFriendly = EntityUid.Invalid;

        if (!ranged.NCAvoidFriendlyFire ||
            ranged.NCFriendlyFireLineRadius <= 0f ||
            !TryComp<NpcFactionMemberComponent>(shooter, out var shooterFaction))
        {
            return false;
        }

        var shotVector = targetWorldPos - shooterWorldPos;
        var shotLength = shotVector.Length();
        if (shotLength <= ranged.NCFriendlyFireIgnoreRange)
            return false;

        var direction = shotVector / shotLength;

        if (NCTryGetFriendlyOnShotRay(shooter, target, shooterFaction, shooterXform.MapID, shooterWorldPos, direction, shotLength, out blockingFriendly))
            return true;

        var searchBox = Box2.FromTwoPoints(shooterWorldPos, targetWorldPos)
            .Enlarged(ranged.NCFriendlyFireLineRadius);

        foreach (var entity in _ncLookup.GetEntitiesIntersecting(shooterXform.MapID, searchBox, LookupFlags.Dynamic))
        {
            if (entity == shooter ||
                entity == target ||
                Deleted(entity) ||
                !TryComp<TransformComponent>(entity, out var otherXform) ||
                otherXform.MapID != shooterXform.MapID)
            {
                continue;
            }

            if (!TryComp<NpcFactionMemberComponent>(entity, out var otherFaction) ||
                !_ncFaction.IsEntityFriendly((shooter, shooterFaction), (entity, otherFaction)))
            {
                continue;
            }

            if (TryComp<MobStateComponent>(entity, out var mobState) && mobState.CurrentState >= MobState.Dead)
                continue;

            var otherPos = _transform.GetWorldPosition(otherXform);
            var relative = otherPos - shooterWorldPos;
            var forwardDistance = Vector2.Dot(relative, direction);

            if (forwardDistance <= ranged.NCFriendlyFireIgnoreRange ||
                forwardDistance >= shotLength)
            {
                continue;
            }

            var closestPoint = shooterWorldPos + direction * forwardDistance;
            if ((otherPos - closestPoint).LengthSquared() <= ranged.NCFriendlyFireLineRadius * ranged.NCFriendlyFireLineRadius)
            {
                blockingFriendly = entity;
                return true;
            }
        }

        return false;
    }

    private bool NCTryGetFriendlyOnShotRay(
        EntityUid shooter,
        EntityUid target,
        NpcFactionMemberComponent shooterFaction,
        MapId mapId,
        Vector2 shooterWorldPos,
        Vector2 direction,
        float shotLength,
        out EntityUid blockingFriendly)
    {
        blockingFriendly = EntityUid.Invalid;

        var ray = new CollisionRay(shooterWorldPos, direction, (int) CollisionGroup.BulletImpassable);
        foreach (var hit in _physics.IntersectRay(mapId, ray, shotLength, shooter, returnOnFirstHit: false)
                     .OrderBy(hit => hit.Distance))
        {
            var entity = hit.HitEntity;

            if (entity == shooter || Deleted(entity))
                continue;

            if (entity == target && !NCIsLiveFriendly(shooter, shooterFaction, entity))
                return false;

            if (NCIsLiveFriendly(shooter, shooterFaction, entity))
            {
                blockingFriendly = entity;
                return true;
            }

            // A solid non-friendly bullet blocker before any friendly means the shot is already blocked.
            return false;
        }

        return false;
    }

    private bool NCIsLiveFriendly(EntityUid shooter, NpcFactionMemberComponent shooterFaction, EntityUid other)
    {
        if (TryComp<MobStateComponent>(other, out var mobState) && mobState.CurrentState >= MobState.Dead)
            return false;

        // Preserve corporate friendlies while peaceful RTS mode swaps the active faction to Passive.
        if (TryComp<RiggerDroneComponent>(shooter, out var drone) &&
            TryComp<NpcFactionMemberComponent>(other, out var droneTargetFaction) &&
            _ncFaction.IsMemberOfAny((other, droneTargetFaction), drone.DroneFactions))
        {
            return true;
        }

        return TryComp<NpcFactionMemberComponent>(other, out var otherFaction) &&
               _ncFaction.IsEntityFriendly((shooter, shooterFaction), (other, otherFaction));
    }

    private void NCRepositionForFriendlyFire(
        EntityUid shooter,
        NPCRangedCombatComponent ranged,
        TransformComponent shooterXform,
        Vector2 shooterWorldPos,
        Vector2 targetWorldPos)
    {
        if (_timing.CurTime < ranged.NCFriendlyFireNextReposition)
            return;

        ranged.NCFriendlyFireNextReposition = _timing.CurTime + TimeSpan.FromSeconds(ranged.NCFriendlyFireRepositionCooldown);

        var shotVector = targetWorldPos - shooterWorldPos;
        if (shotVector.LengthSquared() <= 0.001f)
            return;

        var direction = shotVector.Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var side = _random.Prob(0.5f) ? 1f : -1f;
        var desiredWorld = shooterWorldPos
                           + perpendicular * side * ranged.NCFriendlyFireRepositionDistance
                           - direction * ranged.NCFriendlyFireRepositionBackoff;

        var parentXform = Transform(shooterXform.ParentUid);
        var desiredLocal = Vector2.Transform(desiredWorld, _transform.GetInvWorldMatrix(parentXform));
        var destination = new EntityCoordinates(shooterXform.ParentUid, desiredLocal);

        var steering = _steering.Register(shooter, destination);
        steering.Range = ranged.NCFriendlyFireRepositionRange;
        steering.ForceMove = true;
    }
}
