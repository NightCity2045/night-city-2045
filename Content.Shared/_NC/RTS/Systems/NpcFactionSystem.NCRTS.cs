using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
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
