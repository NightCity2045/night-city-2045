using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Examine;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Content.Server.NPC.HTN;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

public sealed partial class PickCoverOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = "TargetCoordinates";

    [DataField("range")]
    public float Range = 10f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Failed;

        var transformSystem = _entManager.System<SharedTransformSystem>();
        var lookup = _entManager.System<EntityLookupSystem>();
        var examine = _entManager.System<ExamineSystemShared>();

        var ownerXform = _entManager.GetComponent<TransformComponent>(owner);
        var targetXform = _entManager.GetComponent<TransformComponent>(target);
        var targetPos = transformSystem.GetWorldPosition(targetXform);
        var ownerPos = transformSystem.GetWorldPosition(ownerXform);

        // Find potential cover
        var potentialCovers = lookup.GetEntitiesInRange(owner, Range, LookupFlags.Static);
        EntityUid? bestCover = null;
        var bestDist = float.MaxValue;
        Vector2 bestPos = Vector2.Zero;

        foreach (var cover in potentialCovers)
        {
            if (!_entManager.TryGetComponent<PhysicsComponent>(cover, out var physics) || !physics.Hard || !physics.CanCollide)
                continue;

            var coverPos = transformSystem.GetWorldPosition(cover);
            var toTarget = (targetPos - coverPos).Normalized();
            
            // Position behind cover
            var hidePos = coverPos - toTarget * 1.5f; 

            // Check if hidePos is actually hidden from target
            var mapCoords = new MapCoordinates(hidePos, ownerXform.MapID);
            
            if (!examine.InRangeUnOccluded(target, mapCoords, Range * 2, null))
            {
                var dist = (hidePos - ownerPos).Length();
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = hidePos;
                    bestCover = cover;
                }
            }
        }

        if (bestCover != null)
        {
            blackboard.SetValue(TargetCoordinatesKey, new EntityCoordinates(ownerXform.MapUid ?? EntityUid.Invalid, bestPos));
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }
}
