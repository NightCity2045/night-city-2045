using Content.Server._NC.NPC.Components;
using Content.Shared.NPC;
using Content.Server.NPC.HTN;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

public sealed partial class CirclingOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    /// <summary>
    /// How long to circle for.
    /// </summary>
    [DataField("duration")]
    public float Duration = 3f;

    [DataField("idealDistance")]
    public float IdealDistance = 6f;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; set; } = HTNPlanState.PlanFinished;

    private float _timer = 0f;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var move = _entManager.EnsureComponent<NCTacticalMovementComponent>(owner);
        move.Type = NCTacticalMovementType.Circle;
        move.IdealDistance = IdealDistance;
        
        if (blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            move.Target = target;
            
        _timer = 0f;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        _timer += frameTime;
        if (_timer >= Duration)
            return HTNOperatorStatus.Finished;

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Finished;

        return HTNOperatorStatus.Continuing;
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entManager.RemoveComponent<NCTacticalMovementComponent>(owner);
    }
}
