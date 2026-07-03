using Content.Shared._NC.Atmos;
using Content.Shared.Damage;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._NC.Atmos;

public sealed class NCFireSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NCTileFireComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var fire, out var xform))
        {
            fire.Lifetime -= frameTime;
            if (fire.Lifetime <= 0)
            {
                QueueDel(uid);
                continue;
            }

            fire.AccumulatedDamageTimer += frameTime;
            if (fire.AccumulatedDamageTimer >= 1f)
            {
                fire.AccumulatedDamageTimer -= 1f;
                // Deal damage to entities intersecting
                var physicsQuery = GetEntityQuery<PhysicsComponent>();
                if (physicsQuery.TryComp(uid, out var firePhysics))
                {
                    var intersecting = _lookup.GetEntitiesIntersecting(uid, LookupFlags.Dynamic | LookupFlags.Sundries);
                    foreach (var ent in intersecting)
                    {
                        var damage = new DamageSpecifier();
                        damage.DamageDict.Add("Heat", fire.DamagePerSecond);
                        _damageable.TryChangeDamage(ent, damage, origin: uid);
                    }
                }
            }
        }
    }
}
