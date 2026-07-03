using Content.Server._NC.NPC.Components;
using Content.Shared.NPC;
using Content.Server.NPC.HTN;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

public sealed partial class FleeOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("fleeDistance")]
    public float FleeDistance = 15f;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; set; } = HTNPlanState.PlanFinished;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var move = _entManager.EnsureComponent<NCTacticalMovementComponent>(owner);
        move.Type = NCTacticalMovementType.Flee;
        move.IdealDistance = FleeDistance;
        
        if (blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            move.Target = target;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Finished;

        var ownerPos = _entManager.GetComponent<TransformComponent>(owner).WorldPosition;
        var targetPos = _entManager.GetComponent<TransformComponent>(target).WorldPosition;

        if ((ownerPos - targetPos).Length() >= FleeDistance)
            return HTNOperatorStatus.Finished;

        return HTNOperatorStatus.Continuing;
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entManager.RemoveComponent<NCTacticalMovementComponent>(owner);
    }
}
