using Content.Shared._NC.Factions;
using Content.Shared.GameTicking;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Factions;

/// <summary>
/// Applies Night City NPC factions to player mobs from their job department.
/// </summary>
public sealed class NCDepartmentFactionSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.Mob == EntityUid.Invalid || Deleted(args.Mob) || args.JobId == null)
            return;

        if (!TryGetDepartment(args.JobId, out var department) ||
            !TryGetFaction(department, out var faction))
        {
            return;
        }

        ApplyDepartmentFaction(args.Mob, faction);
    }

    private bool TryGetDepartment(ProtoId<JobPrototype> jobId, out ProtoId<DepartmentPrototype> department)
    {
        foreach (var prototype in _prototype.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (!prototype.Roles.Contains(jobId))
                continue;

            department = prototype.ID;
            return true;
        }

        department = default;
        return false;
    }

    private bool TryGetFaction(
        ProtoId<DepartmentPrototype> department,
        out ProtoId<NpcFactionPrototype> faction)
    {
        foreach (var prototype in _prototype.EnumeratePrototypes<NCDepartmentFactionPrototype>())
        {
            if (prototype.Department != department)
                continue;

            faction = prototype.Faction;
            return true;
        }

        faction = default;
        return false;
    }

    private void ApplyDepartmentFaction(EntityUid mob, ProtoId<NpcFactionPrototype> faction)
    {
        var member = EnsureComp<NpcFactionMemberComponent>(mob);

        // Only replace factions owned by this department mapping. Other systems may add
        // temporary or antagonist factions, and those should not be erased on spawn hooks.
        foreach (var managedFaction in GetManagedFactions())
        {
            _npcFaction.RemoveFaction((mob, member), managedFaction, false);
        }

        _npcFaction.AddFaction((mob, member), faction, true);
    }

    private HashSet<ProtoId<NpcFactionPrototype>> GetManagedFactions()
    {
        var factions = new HashSet<ProtoId<NpcFactionPrototype>>();

        foreach (var prototype in _prototype.EnumeratePrototypes<NCDepartmentFactionPrototype>())
        {
            factions.Add(prototype.Faction);
        }

        return factions;
    }
}
