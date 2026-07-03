using System.Numerics;
using Content.Server._NC.NPC.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.Server._NC.NPC.Systems;

public sealed class NCTacticalMovementSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NCTacticalMovementComponent, NPCSteeringEvent>(OnSteering);
    }

    private void OnSteering(EntityUid uid, NCTacticalMovementComponent component, ref NPCSteeringEvent args)
    {
        if (component.Type == NCTacticalMovementType.None || component.Target == null)
            return;

        var target = component.Target.Value;
        if (Deleted(target) || !HasComp<TransformComponent>(target))
            return;

        var worldPos = args.WorldPosition;
        var targetPos = _transform.GetWorldPosition(target);
        var toTarget = targetPos - worldPos;
        var distance = toTarget.Length();

        if (distance == 0)
            toTarget = _random.NextVector2();

        if (component.Type == NCTacticalMovementType.Flee)
        {
            if (distance > component.IdealDistance)
                return;

            var fleeDir = -toTarget.Normalized();
            ApplyInterest(ref args, fleeDir);
            args.Steering.CanSeek = false;
        }
        else if (component.Type == NCTacticalMovementType.Circle)
        {
            // Perpendicular direction
            var circleDir = new Vector2(-toTarget.Y, toTarget.X).Normalized();
            
            // Randomize direction based on UID
            if (((int) uid) % 2 == 0)
                circleDir = -circleDir;

            ApplyInterest(ref args, circleDir);
            
            // Also try to maintain some distance - if too close, push out, if too far, pull in
            if (distance < component.IdealDistance * 0.8f)
            {
                ApplyInterest(ref args, -toTarget.Normalized(), 0.5f);
            }
            else if (distance > component.IdealDistance * 1.2f)
            {
                ApplyInterest(ref args, toTarget.Normalized(), 0.5f);
            }

            args.Steering.CanSeek = false;
        }
    }

    private void ApplyInterest(ref NPCSteeringEvent args, Vector2 direction, float weight = 1.0f)
    {
        var norm = args.OffsetRotation.RotateVec(direction).Normalized();

        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var result = Vector2.Dot(norm, NPCSteeringSystem.Directions[i]) * weight;

            if (result < 0f)
                continue;

            args.Steering.Interest[i] = MathF.Max(args.Steering.Interest[i], result);
        }
    }
}
