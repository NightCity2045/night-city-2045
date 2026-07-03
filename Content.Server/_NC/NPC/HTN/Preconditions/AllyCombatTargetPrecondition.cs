using Content.Server.NPC.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Server.NPC.HTN;
using Robust.Shared.GameObjects;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class AllyCombatTargetPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("range")]
    public float Range = 15f;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var factionSystem = _entManager.System<NpcFactionSystem>();
        var lookup = _entManager.System<EntityLookupSystem>();
        
        foreach (var ally in lookup.GetEntitiesInRange(owner, Range))
        {
            if (ally == owner) continue;
            
            // Explicitly create Entity<T> for the method call
            if (!factionSystem.IsEntityFriendly((owner, null), (ally, null))) continue;

            EntityUid? target = null;
            if (_entManager.TryGetComponent<NPCRangedCombatComponent>(ally, out var ranged))
            {
                target = ranged.Target;
            }
            else if (_entManager.TryGetComponent<NPCMeleeCombatComponent>(ally, out var melee))
            {
                target = melee.Target;
            }

            if (target != null && !_entManager.Deleted(target.Value))
            {
                blackboard.SetValue(TargetKey, target.Value);
                return true;
            }
        }

        return false;
    }
}
