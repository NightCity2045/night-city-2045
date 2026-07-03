using System.Linq;
using Content.Shared._NC.Stats;
using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Movement.Systems;
using Content.Shared.Preferences;

namespace Content.Server._NC.Stats.Systems;

/// <summary>
/// Applies the persistent NC RPG build from the character profile to the live player entity.
/// </summary>
public sealed class NCStatsSpawnSystem : EntitySystem
{
    [Dependency] private readonly SharedNCStatsSystem _stats = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<LoadProfileExtensionsEvent>(OnProfileLoad);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        ApplyProfileStats(args.Mob, args.Profile);
    }

    private void OnProfileLoad(LoadProfileExtensionsEvent args)
    {
        ApplyProfileStats(args.Mob, args.Profile);
    }

    private void ApplyProfileStats(EntityUid uid, HumanoidCharacterProfile profile)
    {
        if (uid == EntityUid.Invalid || Deleted(uid))
            return;

        var stats = EnsureComp<NCStatsComponent>(uid);
        stats.Stats = CloneStats(profile.Stats);
        _stats.RecalculateStats(stats);
        Dirty(uid, stats);

        var skills = EnsureComp<NCSkillsComponent>(uid);
        skills.Skills = CloneSkills(profile.Skills);
        _stats.RecalculateSkills(skills);
        Dirty(uid, skills);

        // Luck is a spendable runtime resource derived from the persistent Luck stat at spawn/profile load.
        var luck = EnsureComp<NCLuckComponent>(uid);
        _stats.SyncLuck(uid, luck);
        Dirty(uid, luck);

        // NC stats affect derived movement modifiers, so recalculate after the profile build is on the mob.
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private static List<NCStatEntry> CloneStats(IEnumerable<NCStatEntry> stats)
    {
        return stats
            .Select(stat => new NCStatEntry(stat.StatId, CloneValue(stat.Value)))
            .ToList();
    }

    private static List<NCSkillEntry> CloneSkills(IEnumerable<NCSkillEntry> skills)
    {
        return skills
            .Select(skill => new NCSkillEntry(skill.SkillId, CloneValue(skill.Value), skill.Specialization))
            .ToList();
    }

    private static NCTrackedValue CloneValue(NCTrackedValue value)
    {
        return new NCTrackedValue
        {
            BaseValue = value.BaseValue,
            ProgressionValue = value.ProgressionValue,
            TemporaryValue = value.TemporaryValue,
            FinalValue = value.FinalValue,
        };
    }
}
