using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Robust.Shared.Map;

namespace Content.Server._NC.NPC.HTN.Preconditions;

/// <summary>
///     _NC: Custom range check that uses literal float values instead of blackboard keys.
///     Perfect for tactical maneuvering checks like circling.
/// </summary>
public sealed partial class NCTargetInRangePrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")] 
    public string TargetKey = "Target";

    [DataField("minRange")]
    public float MinRange = 0f;

    [DataField("maxRange")]
    public float MaxRange = 5f;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var ownerXform) ||
            !_entManager.TryGetComponent<TransformComponent>(target, out var targetXform))
            return false;

        var distance = (ownerXform.WorldPosition - targetXform.WorldPosition).Length();

        return distance >= MinRange && distance <= MaxRange;
    }
}
