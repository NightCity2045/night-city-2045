namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Rejects stale HTN entity targets before engine preconditions resolve their components.
/// </summary>
public sealed partial class NCValidEntityTargetPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) &&
               target.IsValid() &&
               _entManager.EntityExists(target) &&
               _entManager.HasComponent<TransformComponent>(target);
    }
}
