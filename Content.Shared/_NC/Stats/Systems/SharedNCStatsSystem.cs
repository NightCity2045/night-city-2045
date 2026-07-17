using System.Linq;
using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Events;
using Content.Shared._NC.Stats.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Stats.Systems;

/// <summary>
/// Normalizes and reads the RPG stat, skill and luck data contract.
/// </summary>
public sealed class SharedNCStatsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCStatsComponent, MapInitEvent>(OnStatsMapInit);
        SubscribeLocalEvent<NCStatsComponent, AfterAutoHandleStateEvent>(OnStatsHandleState);

        SubscribeLocalEvent<NCSkillsComponent, MapInitEvent>(OnSkillsMapInit);
        SubscribeLocalEvent<NCSkillsComponent, AfterAutoHandleStateEvent>(OnSkillsHandleState);

        SubscribeLocalEvent<NCLuckComponent, MapInitEvent>(OnLuckMapInit);
    }

    private void OnStatsMapInit(EntityUid uid, NCStatsComponent component, MapInitEvent args)
    {
        RecalculateStats(component);
    }

    private void OnStatsHandleState(EntityUid uid, NCStatsComponent component, ref AfterAutoHandleStateEvent args)
    {
        RecalculateStats(component);
        RaiseLocalEvent(uid, new NCStatsStateHandledEvent());
    }

    private void OnSkillsMapInit(EntityUid uid, NCSkillsComponent component, MapInitEvent args)
    {
        RecalculateSkills(component);
    }

    private void OnSkillsHandleState(EntityUid uid, NCSkillsComponent component, ref AfterAutoHandleStateEvent args)
    {
        RecalculateSkills(component);
        RaiseLocalEvent(uid, new NCStatsStateHandledEvent());
    }

    private void OnLuckMapInit(EntityUid uid, NCLuckComponent component, MapInitEvent args)
    {
        SyncLuck(uid, component);
    }

    public void RecalculateStats(NCStatsComponent component)
    {
        component.StatCache.Clear();

        foreach (var entry in component.Stats)
        {
            RecalculateStatEntry(entry);
            component.StatCache[entry.StatId] = entry.Value.FinalValue;
        }
    }

    public void RecalculateSkills(NCSkillsComponent component)
    {
        component.SkillCache.Clear();

        foreach (var entry in component.Skills)
        {
            entry.Value.FinalValue = entry.Value.BaseValue + entry.Value.ProgressionValue + entry.Value.TemporaryValue;

            if (_prototype.TryIndex<NCSkillPrototype>(entry.SkillId, out var proto))
                entry.Value.FinalValue = Math.Clamp(entry.Value.FinalValue, proto.MinValue, proto.MaxValue);

            component.SkillCache[entry.SkillId] = entry.Value.FinalValue;
        }
    }

    public void SyncLuck(EntityUid uid, NCLuckComponent component)
    {
        if (TryComp<NCStatsComponent>(uid, out var stats) &&
            TryGetStatValue(stats, NCStatIds.Luck, out var luckStat))
        {
            if (component.Max <= 0)
                component.Max = luckStat;

            if (component.Current <= 0)
                component.Current = component.Max;
        }

        component.Max = Math.Max(0, component.Max);
        component.Current = Math.Clamp(component.Current, 0, component.Max);
    }

    public List<NCStatEntry> CreateDefaultStats(int defaultBaseValue = 1)
    {
        var stats = new List<NCStatEntry>();

        foreach (var proto in _prototype.EnumeratePrototypes<NCStatPrototype>().OrderBy(p => p.ID))
        {
            var tracked = new NCTrackedValue(Math.Clamp(defaultBaseValue, proto.MinValue, proto.MaxValue));
            stats.Add(new NCStatEntry(proto.ID, tracked));
        }

        return stats;
    }

    public List<NCSkillEntry> CreateDefaultSkills(bool mandatoryOnly = false)
    {
        var skills = new List<NCSkillEntry>();

        foreach (var proto in _prototype.EnumeratePrototypes<NCSkillPrototype>().OrderBy(p => p.ID))
        {
            if (mandatoryOnly && !proto.MandatoryForCharacters)
                continue;

            skills.Add(new NCSkillEntry(proto.ID, new NCTrackedValue(proto.DefaultBaseValue)));
        }

        return skills;
    }

    public bool SetStatBaseValue(EntityUid uid, string statId, int baseValue, NCStatsComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        foreach (var entry in component.Stats)
        {
            if (!string.Equals(entry.StatId, statId, StringComparison.Ordinal))
                continue;

            var oldFinalValue = entry.Value.FinalValue;
            entry.Value.BaseValue = ClampStatValue(entry.StatId, baseValue);
            RecalculateStatEntry(entry);

            // Keep the O(1) cache in sync after a single-entry recalc.
            if (component.StatCache.ContainsKey(entry.StatId))
                component.StatCache[entry.StatId] = entry.Value.FinalValue;

            // Runtime stat mutations must go through this path so clients and dependent systems see the new value.
            Dirty(uid, component);

            if (oldFinalValue != entry.Value.FinalValue)
            {
                var ev = new NCStatChangedEvent(entry.StatId, oldFinalValue, entry.Value.FinalValue);
                RaiseLocalEvent(uid, ref ev);
            }

            return true;
        }

        return false;
    }

    public bool TryGetStatValue(NCStatsComponent component, string statId, out int value)
    {
        // O(1) dictionary lookup instead of O(N) list scan.
        return component.StatCache.TryGetValue(statId, out value);
    }

    public bool TryGetSkillValue(NCSkillsComponent component, string skillId, out int value)
    {
        // O(1) dictionary lookup instead of O(N) list scan.
        return component.SkillCache.TryGetValue(skillId, out value);
    }

    private void RecalculateStatEntry(NCStatEntry entry)
    {
        entry.Value.FinalValue = ClampStatValue(
            entry.StatId,
            entry.Value.BaseValue + entry.Value.ProgressionValue + entry.Value.TemporaryValue);
    }

    private int ClampStatValue(string statId, int value)
    {
        return _prototype.TryIndex<NCStatPrototype>(statId, out var proto)
            ? Math.Clamp(value, proto.MinValue, proto.MaxValue)
            : value;
    }
}
