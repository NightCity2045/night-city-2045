using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._NC.Weapons.Ranged.SlowOnShoot;

public sealed class SharedSlowOnShootSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<SlowOnShootComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ShootingSlowdownComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    private void OnGunShot(EntityUid uid, SlowOnShootComponent component, ref GunShotEvent args)
    {
        var user = args.User;
        if (user == EntityUid.Invalid)
            return;

        var slowdown = EnsureComp<ShootingSlowdownComponent>(user);
        
        // We overwrite the slowdown if the new one is more severe or if there wasn't one
        slowdown.WalkModifier = component.WalkModifier;
        slowdown.SprintModifier = component.SprintModifier;
        slowdown.EndTime = _timing.CurTime + component.Duration;
        
        Dirty(user, slowdown);
        _movementSpeed.RefreshMovementSpeedModifiers(user);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, ShootingSlowdownComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.WalkModifier, component.SprintModifier);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ShootingSlowdownComponent>();
        
        while (query.MoveNext(out var uid, out var comp))
        {
            if (curTime >= comp.EndTime)
            {
                RemComp<ShootingSlowdownComponent>(uid);
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }
        }
    }
}
