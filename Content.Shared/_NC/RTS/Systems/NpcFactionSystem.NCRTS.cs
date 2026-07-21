using Content.Shared._NC.Rigger.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    /// <summary>
    /// Uses a drone's stable command faction before its temporary aggression faction.
    /// Every RTS combat path must use this method so targeting, retaliation and shot safety agree.
    /// </summary>
    public bool NCIsRtsFriendly(EntityUid source, EntityUid target)
    {
        if (TryComp<RiggerDroneComponent>(source, out var sourceDrone))
        {
            if (TryComp<RiggerDroneComponent>(target, out var targetDrone) &&
                sourceDrone.DroneFactions.Overlaps(targetDrone.DroneFactions))
            {
                return true;
            }

            if (TryComp<NpcFactionMemberComponent>(target, out var targetFaction) &&
                IsMemberOfAny((target, targetFaction), sourceDrone.DroneFactions))
            {
                return true;
            }
        }

        return IsEntityFriendly(source, target);
    }

    /// <summary>
    /// Adds temporary RTS hostility against every known NPC faction except the
    /// entity's own factions. The entity keeps its normal faction membership so
    /// same-faction units remain friendly through IsEntityFriendly().
    /// </summary>
    public void NCApplyAggressiveRtsHostiles(
        Entity<NpcFactionMemberComponent> ent,
        IReadOnlySet<ProtoId<NpcFactionPrototype>> excludedFactions)
    {
        foreach (var prototype in _proto.EnumeratePrototypes<NpcFactionPrototype>())
        {
            var factionId = new ProtoId<NpcFactionPrototype>(prototype.ID);
            if (ent.Comp.Factions.Contains(factionId) || excludedFactions.Contains(factionId))
                continue;

            ent.Comp.HostileFactions.Add(factionId);
            ent.Comp.FriendlyFactions.Remove(factionId);
        }
    }
}
