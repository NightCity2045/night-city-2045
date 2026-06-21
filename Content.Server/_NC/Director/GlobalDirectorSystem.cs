using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.Announcements.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.Director;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NC.Director;

/// <summary>
/// Manages the Global Director system and advances active scenario events.
/// The system keeps authoring data in YAML while enforcing runtime safety in code.
/// </summary>
public sealed class GlobalDirectorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AnnouncerSystem _announcer = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NpcFactionSystem _faction = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedGameTicker _gameTicker = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("director");

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
        SubscribeLocalEvent<DirectorSpawneeComponent, DamageChangedEvent>(OnSpawneeDamageChanged);
        SubscribeLocalEvent<DirectorSpawneeComponent, AttackedEvent>(OnSpawneeAttacked);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Scheduler pass: each director rule entity may attempt to start one event.
        var directorQuery = EntityQueryEnumerator<GlobalDirectorComponent>();
        while (directorQuery.MoveNext(out var uid, out var director))
        {
            if (!director.Enabled)
                continue;

            if (_timing.CurTime < director.NextCheckTime)
                continue;

            if (TryStartRandomEvent(director, out var startedProto))
                _sawmill.Info($"Director started random event {startedProto}.");

            ResetDirectorTimer(director);
            Dirty(uid, director);
        }

        // Runtime pass: timers advance already running events.
        var eventQuery = EntityQueryEnumerator<DirectorEventComponent>();
        while (eventQuery.MoveNext(out var uid, out var directorEvent))
        {
            if (TryProcessCleanupRetreat(uid, directorEvent))
                continue;

            if (directorEvent.PhaseEndTime != null && _timing.CurTime >= directorEvent.PhaseEndTime)
                AdvancePhase(uid, directorEvent);
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        if (!TryComp<DirectorSpawneeComponent>(ev.Target, out var spawnee))
            return;

        if (!TryComp<DirectorEventComponent>(spawnee.EventEntity, out var directorEvent))
            return;

        // Trigger processing is scoped to entities spawned for the currently active phase.
        if (spawnee.PhaseSequence != directorEvent.PhaseSequence)
            return;

        if (CheckTriggers(spawnee.EventEntity, directorEvent, DirectorTriggerType.MobKilled, ev.Target, spawnee))
            AdvancePhase(spawnee.EventEntity, directorEvent);
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent ev)
    {
        if (!TryComp<DirectorSpawneeComponent>(ev.Entity, out var spawnee))
            return;

        if (!TryComp<DirectorEventComponent>(spawnee.EventEntity, out var directorEvent))
            return;

        directorEvent.SpawnedEntities.Remove(ev.Entity);

        // Cleanup and external deletion may terminate entities from older phases.
        if (spawnee.PhaseSequence != directorEvent.PhaseSequence)
        {
            Dirty(spawnee.EventEntity, directorEvent);
            return;
        }

        if (CheckTriggers(spawnee.EventEntity, directorEvent, DirectorTriggerType.EntityDestroyed, ev.Entity, spawnee))
            AdvancePhase(spawnee.EventEntity, directorEvent);

        Dirty(spawnee.EventEntity, directorEvent);
    }

    private void OnSpawneeDamageChanged(Entity<DirectorSpawneeComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin)
            return;

        if (TryResolveAggressor(origin, out var aggressor))
            TryHandleExternalAggression(ent, ent.Comp, aggressor);
    }

    private void OnSpawneeAttacked(Entity<DirectorSpawneeComponent> ent, ref AttackedEvent args)
    {
        TryHandleExternalAggression(ent, ent.Comp, args.User);
    }

    private bool CheckTriggers(
        EntityUid eventUid,
        DirectorEventComponent component,
        DirectorTriggerType type,
        EntityUid targetUid,
        DirectorSpawneeComponent spawnee)
    {
        if (!_prototype.TryIndex<DirectorEventPrototype>(component.PrototypeId, out var proto))
            return false;

        if (component.CurrentPhase == null || !proto.Phases.TryGetValue(component.CurrentPhase, out var phase))
            return false;

        var targetProto = MetaData(targetUid).EntityPrototype?.ID;

        foreach (var trigger in phase.Triggers)
        {
            if (trigger.Type != type)
                continue;

            trigger.NormalizeLegacyFields();

            if (trigger.TargetPrototype != null && trigger.TargetPrototype != targetProto)
                continue;

            if (trigger.TargetGroup != null && trigger.TargetGroup != spawnee.GroupTag)
                continue;

            // The composite key keeps counters isolated per trigger filter.
            var key = $"{type}:{trigger.TargetPrototype ?? "any"}:{trigger.TargetGroup ?? "any"}";
            component.TriggerCounters.TryGetValue(key, out var currentCount);
            component.TriggerCounters[key] = ++currentCount;

            Dirty(eventUid, component);

            if (currentCount >= trigger.Count)
                return true;
        }

        return false;
    }

    private void ResetDirectorTimer(GlobalDirectorComponent director)
    {
        var min = director.MinDelay;
        var max = director.MaxDelay < director.MinDelay ? director.MinDelay : director.MaxDelay;
        var delay = _random.Next(min, max);
        director.NextCheckTime = _timing.CurTime + delay;
    }

    /// <summary>
    /// Attempts to start a random eligible event.
    /// </summary>
    public bool TryStartRandomEvent(GlobalDirectorComponent director, out string? startedPrototypeId)
    {
        startedPrototypeId = null;

        if (_playerManager.PlayerCount < director.MinPlayers)
            return false;

        if (_gameTicker.RoundDuration() < director.RoundStartDelay)
            return false;

        if (CountActiveEvents() >= director.MaxConcurrentEvents)
            return false;

        var eligible = new List<DirectorEventPrototype>();
        foreach (var proto in _prototype.EnumeratePrototypes<DirectorEventPrototype>())
        {
            if (CanStartPrototype(proto, director, out _))
                eligible.Add(proto);
        }

        if (eligible.Count == 0)
            return false;

        var totalWeight = eligible.Sum(p => p.Weight);
        if (totalWeight <= 0)
            return false;

        var pick = _random.NextFloat(totalWeight);
        foreach (var proto in eligible)
        {
            pick -= proto.Weight;
            if (pick > 0)
                continue;

            StartEvent(proto, director);
            startedPrototypeId = proto.ID;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts a specific director event and records the scheduler history.
    /// </summary>
    public EntityUid StartEvent(DirectorEventPrototype proto, GlobalDirectorComponent? director = null)
    {
        var uid = EntityManager.SpawnEntity(null, MapCoordinates.Nullspace);
        var directorEvent = EnsureComp<DirectorEventComponent>(uid);
        directorEvent.PrototypeId = proto.ID;
        directorEvent.CurrentPhase = null;
        directorEvent.PhaseEndTime = null;
        directorEvent.PhaseSequence = 0;
        directorEvent.TriggerCounters.Clear();

        director ??= GetFirstDirector();
        if (director != null)
        {
            director.LastRunAt[proto.ID] = _gameTicker.RoundDuration();
            director.RunCounts.TryGetValue(proto.ID, out var count);
            director.RunCounts[proto.ID] = count + 1;
        }

        AdvancePhase(uid, directorEvent);
        return uid;
    }

    /// <summary>
    /// Force-advances an event to its next phase or ends it if there is no valid successor.
    /// </summary>
    public void AdvancePhase(EntityUid uid, DirectorEventComponent? component = null, string? forcedPhaseId = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_prototype.TryIndex<DirectorEventPrototype>(component.PrototypeId, out var proto))
        {
            _sawmill.Error($"Failed to find prototype {component.PrototypeId} for event {ToPrettyString(uid)}.");
            FinishEvent(uid, component, null);
            return;
        }

        string? nextPhaseId = null;
        DirectorPhase? currentPhaseData = null;

        if (forcedPhaseId != null)
        {
            nextPhaseId = forcedPhaseId;
        }
        else if (component.CurrentPhase == null)
        {
            nextPhaseId = proto.StartPhase;
        }
        else if (proto.Phases.TryGetValue(component.CurrentPhase, out currentPhaseData))
        {
            // Cleanup is phase-defined so content authors can decide what persists into the next beat.
            if (currentPhaseData.Cleanup)
                CleanupSpawnedEntities(component);

            nextPhaseId = PickWeightedPhase(currentPhaseData.NextPhases);
        }

        if (nextPhaseId == null || !proto.Phases.TryGetValue(nextPhaseId, out var nextPhaseData))
        {
            _sawmill.Info($"Event {proto.Name} ({uid}) finished.");
            FinishEvent(uid, component, proto);
            return;
        }

        component.CurrentPhase = nextPhaseId;
        component.PhaseSequence++;
        component.TriggerCounters.Clear();

        var (announcerId, announcementColor) = GetAnnouncementConfig(proto);

        if (nextPhaseData.Spawns.Count > 0)
            SpawnPhaseEntities(uid, component, proto, nextPhaseId, nextPhaseData);

        ApplyFactionOverrides(component, nextPhaseData);
        ApplyPhaseAggro(component, nextPhaseData);
        ApplyAiDomain(component, nextPhaseData, proto, uid);

        if (nextPhaseData.Announcement != null)
        {
            _announcer.SendAnnouncement(
                announcerId,
                Loc.GetString(nextPhaseData.Announcement),
                colorOverride: announcementColor);
        }

        component.PhaseEndTime = nextPhaseData.Duration != null
            ? _timing.CurTime + nextPhaseData.Duration.Value
            : null;

        Dirty(uid, component);
        _sawmill.Debug($"Event {proto.Name} ({uid}) advanced to phase {component.CurrentPhase}: {nextPhaseData.Name}");
    }

    public bool CancelEvent(EntityUid uid)
    {
        if (!TryComp<DirectorEventComponent>(uid, out var component))
            return false;

        _sawmill.Info($"Event {uid} cancelled.");
        FinishEvent(uid, component, null);
        return true;
    }

    public IEnumerable<string> GetStatusReport()
    {
        var director = GetFirstDirector();
        if (director == null)
        {
            yield return "No active GlobalDirector gamerule entity was found.";
            yield break;
        }

        yield return $"Director enabled: {director.Enabled}";
        yield return $"Players: {_playerManager.PlayerCount}";
        yield return $"Round duration: {_gameTicker.RoundDuration()}";
        yield return $"Next scheduler check: {director.NextCheckTime}";
        yield return $"Active events: {CountActiveEvents()} / {director.MaxConcurrentEvents}";

        var activeEvents = EntityQueryEnumerator<DirectorEventComponent>();
        while (activeEvents.MoveNext(out var uid, out var evComp))
        {
            yield return $"Active event {uid}: {evComp.PrototypeId}, phase={evComp.CurrentPhase ?? "<none>"}, phaseSequence={evComp.PhaseSequence}, spawned={evComp.SpawnedEntities.Count}";
        }

        foreach (var proto in _prototype.EnumeratePrototypes<DirectorEventPrototype>().OrderBy(p => p.ID))
        {
            var eligible = CanStartPrototype(proto, director, out var reason);
            yield return eligible
                ? $"Eligible: {proto.ID}"
                : $"Ineligible: {proto.ID} - {reason}";
        }
    }

    private bool CanStartPrototype(DirectorEventPrototype proto, GlobalDirectorComponent director, out string reason)
    {
        reason = "unknown";

        if (!proto.Enabled)
        {
            reason = "prototype is disabled";
            return false;
        }

        if (proto.Abstract)
        {
            reason = "prototype is abstract";
            return false;
        }

        if (proto.Weight <= 0)
        {
            reason = "weight is zero";
            return false;
        }

        if (_playerManager.PlayerCount < proto.MinPlayers)
        {
            reason = $"player count below minimum ({proto.MinPlayers})";
            return false;
        }

        if (proto.MaxPlayers is { } maxPlayers && _playerManager.PlayerCount > maxPlayers)
        {
            reason = $"player count above maximum ({maxPlayers})";
            return false;
        }

        if (_gameTicker.RoundDuration() < proto.MinRoundDuration)
        {
            reason = $"round duration below minimum ({proto.MinRoundDuration})";
            return false;
        }

        if (proto.MaxOccurrences > 0 &&
            director.RunCounts.TryGetValue(proto.ID, out var runCount) &&
            runCount >= proto.MaxOccurrences)
        {
            reason = $"max occurrences reached ({proto.MaxOccurrences})";
            return false;
        }

        if (proto.Cooldown > TimeSpan.Zero &&
            director.LastRunAt.TryGetValue(proto.ID, out var lastRun) &&
            _gameTicker.RoundDuration() - lastRun < proto.Cooldown)
        {
            reason = $"cooldown active ({proto.Cooldown - (_gameTicker.RoundDuration() - lastRun)} remaining)";
            return false;
        }

        if (CountActiveInstances(proto.ID) >= proto.MaxSimultaneous)
        {
            reason = $"max simultaneous instances reached ({proto.MaxSimultaneous})";
            return false;
        }

        if (!proto.Phases.TryGetValue(proto.StartPhase, out var startPhase))
        {
            reason = $"missing start phase '{proto.StartPhase}'";
            return false;
        }

        if (startPhase.Spawns.Count > 0)
        {
            if (!HasSpawnLocation(startPhase.MeetLocationTags))
            {
                reason = "no matching meetLocationTags spawn point on the loaded map";
                return false;
            }

            if (startPhase.EntryLocationTags.Count > 0 && !HasSpawnLocation(startPhase.EntryLocationTags))
            {
                reason = "no matching entryLocationTags spawn point on the loaded map";
                return false;
            }
        }

        reason = "eligible";
        return true;
    }

    private (string AnnouncerId, Color AnnouncementColor) GetAnnouncementConfig(DirectorEventPrototype proto)
    {
        var director = GetFirstDirector();
        if (director == null)
            return (proto.AnnouncerId ?? "Director", proto.AnnouncementColor ?? Color.Cyan);

        return (proto.AnnouncerId ?? director.DefaultAnnouncerId, proto.AnnouncementColor ?? director.AnnouncementColor);
    }

    private void SpawnPhaseEntities(
        EntityUid eventUid,
        DirectorEventComponent component,
        DirectorEventPrototype proto,
        string phaseId,
        DirectorPhase phase)
    {
        var anchorCoords = GetSpawnLocation(phase.MeetLocationTags, null);

        if (anchorCoords == MapCoordinates.Nullspace && phase.Spawns.Count > 0)
        {
            _sawmill.Warning(
                $"Could not find a valid anchor location for event {proto.Name} ({eventUid}) phase {phaseId}. " +
                $"meetLocationTags=[{string.Join(", ", phase.MeetLocationTags)}]");
            return;
        }

        foreach (var group in phase.Spawns)
        {
            var spawnCoordsBase = ResolveGroupSpawnLocation(group, phase, anchorCoords);
            if (spawnCoordsBase == MapCoordinates.Nullspace)
            {
                _sawmill.Warning(
                    $"Could not find a valid group spawn location for event {proto.Name} ({eventUid}) phase {phaseId}, " +
                    $"group={group.GroupTag ?? "<none>"}, groupEntryLocationTags=[{string.Join(", ", group.EntryLocationTags)}], " +
                    $"phaseEntryLocationTags=[{string.Join(", ", phase.EntryLocationTags)}], meetLocationTags=[{string.Join(", ", phase.MeetLocationTags)}]");
                continue;
            }

            var retreatCoordsBase = ResolveGroupRetreatLocation(group, phase, anchorCoords, spawnCoordsBase);

            for (var i = 0; i < group.Amount; i++)
            {
                // Light spread prevents stacked NPCs while keeping the scene coherent.
                var offset = _random.NextVector2(1.0f, 2.5f);
                var finalSpawnCoords = new MapCoordinates(spawnCoordsBase.Position + offset, spawnCoordsBase.MapId);

                var spawned = EntityManager.SpawnEntity(group.Prototype, finalSpawnCoords);
                var spawnee = EnsureComp<DirectorSpawneeComponent>(spawned);
                spawnee.EventEntity = eventUid;
                spawnee.GroupTag = group.GroupTag;
                spawnee.PhaseId = phaseId;
                spawnee.PhaseSequence = component.PhaseSequence;
                spawnee.RetreatMapCoordinates = retreatCoordsBase != MapCoordinates.Nullspace ? retreatCoordsBase : null;
                component.SpawnedEntities.Add(spawned);

                if (group.Faction != null)
                {
                    _faction.ClearFactions(spawned);
                    _faction.AddFaction(spawned, group.Faction.Value);
                }

                // Director scenes are authored in map-space, then translated into per-NPC navigation coordinates.
                if (anchorCoords != MapCoordinates.Nullspace && TryComp<HTNComponent>(spawned, out var htn))
                {
                    var targetNavigationCoords = ToNavigationCoordinates(spawned, anchorCoords);
                    var retreatTarget = spawnee.RetreatMapCoordinates ?? spawnCoordsBase;
                    var retreatNavigationCoords = ToNavigationCoordinates(spawned, retreatTarget);

                    htn.Blackboard.SetValue("TargetCoordinates", targetNavigationCoords);
                    htn.Blackboard.SetValue("FollowTarget", targetNavigationCoords);
                    htn.Blackboard.SetValue("RetreatCoordinates", retreatNavigationCoords);
                    _htn.Replan(htn);
                }
            }
        }
    }

    private MapCoordinates ResolveGroupSpawnLocation(
        DirectorSpawnGroup group,
        DirectorPhase phase,
        MapCoordinates anchorCoords)
    {
        // Group-specific entry points take precedence over phase-wide entry tags.
        if (group.EntryLocationTags.Count > 0)
        {
            var groupEntry = GetSpawnLocation(group.EntryLocationTags, null, anchorCoords != MapCoordinates.Nullspace ? anchorCoords : null);
            if (groupEntry != MapCoordinates.Nullspace)
                return groupEntry;
        }

        // Phase-wide entry tags are the fallback for groups that do not define their own entry route.
        if (phase.EntryLocationTags.Count > 0)
        {
            var phaseEntry = GetSpawnLocation(phase.EntryLocationTags, null, anchorCoords != MapCoordinates.Nullspace ? anchorCoords : null);
            if (phaseEntry != MapCoordinates.Nullspace)
                return phaseEntry;
        }

        // If no dedicated entry exists, use the anchor itself.
        return anchorCoords;
    }

    private MapCoordinates ResolveGroupRetreatLocation(
        DirectorSpawnGroup group,
        DirectorPhase phase,
        MapCoordinates anchorCoords,
        MapCoordinates spawnCoordsBase)
    {
        if (group.ExitLocationTags.Count > 0)
        {
            var retreat = GetSpawnLocation(group.ExitLocationTags, null, anchorCoords != MapCoordinates.Nullspace ? anchorCoords : null);
            if (retreat != MapCoordinates.Nullspace)
                return retreat;
        }

        // Old content did not author a dedicated retreat marker, so preserve current behavior.
        return spawnCoordsBase != MapCoordinates.Nullspace ? spawnCoordsBase : anchorCoords;
    }

    private void ApplyFactionOverrides(DirectorEventComponent component, DirectorPhase phase)
    {
        foreach (var (tag, faction) in phase.FactionOverrides)
        {
            foreach (var entity in component.SpawnedEntities.ToArray())
            {
                if (!TryComp<DirectorSpawneeComponent>(entity, out var spawnee) || spawnee.GroupTag != tag)
                    continue;

                _faction.ClearFactions(entity);
                _faction.AddFaction(entity, faction);
            }
        }
    }

    private void ApplyPhaseAggro(DirectorEventComponent component, DirectorPhase phase)
    {
        if (phase.FactionOverrides.Count < 2)
            return;

        var groups = new Dictionary<string, List<EntityUid>>();

        foreach (var entity in component.SpawnedEntities.ToArray())
        {
            if (!TryComp<DirectorSpawneeComponent>(entity, out var spawnee) ||
                spawnee.GroupTag == null ||
                !phase.FactionOverrides.ContainsKey(spawnee.GroupTag))
            {
                continue;
            }

            if (!groups.TryGetValue(spawnee.GroupTag, out var members))
            {
                members = new List<EntityUid>();
                groups[spawnee.GroupTag] = members;
            }

            members.Add(entity);
        }

        foreach (var (sourceTag, sourceMembers) in groups)
        {
            if (!phase.FactionOverrides.TryGetValue(sourceTag, out var sourceFaction))
                continue;

            foreach (var (targetTag, targetMembers) in groups)
            {
                if (sourceTag == targetTag ||
                    !phase.FactionOverrides.TryGetValue(targetTag, out var targetFaction) ||
                    !_faction.IsFactionHostile(sourceFaction, targetFaction))
                {
                    continue;
                }

                // Explicit aggro prevents scripted firefights from stalling while NPCs wait for a first natural detection tick.
                foreach (var source in sourceMembers)
                {
                    _faction.AggroEntities(source, targetMembers);
                }
            }
        }
    }

    private bool TryHandleExternalAggression(EntityUid attackedUid, DirectorSpawneeComponent spawnee, EntityUid aggressor)
    {
        if (!_playerManager.TryGetSessionByEntity(aggressor, out _))
            return false;

        if (!TryComp<DirectorEventComponent>(spawnee.EventEntity, out var directorEvent) ||
            spawnee.PhaseSequence != directorEvent.PhaseSequence ||
            !_prototype.TryIndex<DirectorEventPrototype>(directorEvent.PrototypeId, out var proto) ||
            directorEvent.CurrentPhase == null ||
            !proto.Phases.TryGetValue(directorEvent.CurrentPhase, out var phase) ||
            phase.ExternalAggressionPhase == null)
        {
            return false;
        }

        // Everyone still participating in the current beat should recognize the attacker before the scene escalates.
        AggroCurrentPhaseAgainstAttacker(directorEvent, aggressor);

        if (directorEvent.CurrentPhase == phase.ExternalAggressionPhase)
            return true;

        _sawmill.Debug(
            $"Director event {proto.ID} escalated from phase {directorEvent.CurrentPhase} to {phase.ExternalAggressionPhase} " +
            $"after external aggression by {ToPrettyString(aggressor)} against {ToPrettyString(attackedUid)}.");

        AdvancePhase(spawnee.EventEntity, directorEvent, phase.ExternalAggressionPhase);
        return true;
    }

    private void AggroCurrentPhaseAgainstAttacker(DirectorEventComponent component, EntityUid aggressor)
    {
        foreach (var entity in component.SpawnedEntities.ToArray())
        {
            if (!TryComp<DirectorSpawneeComponent>(entity, out var spawnee) ||
                spawnee.PhaseSequence != component.PhaseSequence)
            {
                continue;
            }

            _faction.AggroEntity(entity, aggressor);
        }
    }

    private bool TryResolveAggressor(EntityUid origin, out EntityUid aggressor)
    {
        if (_playerManager.TryGetSessionByEntity(origin, out _))
        {
            aggressor = origin;
            return true;
        }

        if (TryComp<ProjectileComponent>(origin, out var projectile) &&
            projectile.Shooter is { } shooter &&
            _playerManager.TryGetSessionByEntity(shooter, out _))
        {
            aggressor = shooter;
            return true;
        }

        aggressor = default;
        return false;
    }

    private void ApplyAiDomain(
        DirectorEventComponent component,
        DirectorPhase phase,
        DirectorEventPrototype proto,
        EntityUid eventUid)
    {
        if (phase.AiDomain == null)
            return;

        if (!_prototype.TryIndex<HTNCompoundPrototype>(phase.AiDomain, out var domain))
        {
            _sawmill.Warning($"Event {proto.Name} ({eventUid}) requested unknown HTN domain '{phase.AiDomain}'.");
            return;
        }

        foreach (var entity in component.SpawnedEntities)
        {
            if (!TryComp<HTNComponent>(entity, out var htn))
                continue;

            htn.RootTask = new HTNCompoundTask { Task = domain.ID };
            _htn.Replan(htn);
        }
    }

    private void FinishEvent(EntityUid uid, DirectorEventComponent component, DirectorEventPrototype? proto)
    {
        if (proto?.CleanupOnEnd ?? true)
            CleanupSpawnedEntities(component);

        EntityManager.DeleteEntity(uid);
    }

    private void CleanupSpawnedEntities(DirectorEventComponent component)
    {
        foreach (var entity in component.SpawnedEntities.ToArray())
        {
            if (Deleted(entity))
                continue;

            EntityManager.DeleteEntity(entity);
        }

        component.SpawnedEntities.Clear();
    }

    private bool TryProcessCleanupRetreat(EntityUid eventUid, DirectorEventComponent component)
    {
        if (!_prototype.TryIndex<DirectorEventPrototype>(component.PrototypeId, out var proto) ||
            component.CurrentPhase == null ||
            !proto.Phases.TryGetValue(component.CurrentPhase, out var phase) ||
            !phase.Cleanup)
        {
            return false;
        }

        var removedAny = false;

        foreach (var entity in component.SpawnedEntities.ToArray())
        {
            if (!TryComp<DirectorSpawneeComponent>(entity, out var spawnee) ||
                spawnee.PhaseSequence != component.PhaseSequence ||
                spawnee.RetreatMapCoordinates == null)
            {
                continue;
            }

            var currentCoords = _transform.GetMapCoordinates(entity);
            var retreatCoords = spawnee.RetreatMapCoordinates.Value;

            if (currentCoords.MapId != retreatCoords.MapId)
                continue;

            // A small map-space threshold is enough; the HTN move task already handles pathing precision.
            if ((currentCoords.Position - retreatCoords.Position).Length() > 1.5f)
                continue;

            EntityManager.DeleteEntity(entity);
            removedAny = true;
        }

        if (component.SpawnedEntities.Count == 0)
        {
            FinishEvent(eventUid, component, proto);
            return true;
        }

        return removedAny;
    }

    private GlobalDirectorComponent? GetFirstDirector()
    {
        var query = EntityQueryEnumerator<GlobalDirectorComponent>();
        return query.MoveNext(out _, out var director) ? director : null;
    }

    private int CountActiveEvents()
    {
        var count = 0;
        var query = EntityQueryEnumerator<DirectorEventComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    private int CountActiveInstances(string prototypeId)
    {
        var count = 0;
        var query = EntityQueryEnumerator<DirectorEventComponent>();
        while (query.MoveNext(out _, out var evComp))
        {
            if (evComp.PrototypeId == prototypeId)
                count++;
        }

        return count;
    }

    private string? PickWeightedPhase(Dictionary<string, float> nextPhases)
    {
        if (nextPhases.Count == 0)
            return null;

        var totalWeight = nextPhases.Values.Sum();
        if (totalWeight <= 0)
            return null;

        var pick = _random.NextFloat(totalWeight);
        foreach (var (id, weight) in nextPhases)
        {
            pick -= weight;
            if (pick <= 0)
                return id;
        }

        return null;
    }

    private EntityUid? GetSectorForCoordinates(MapCoordinates coords)
    {
        var worldPos = coords.Position;
        var sectorQuery = EntityQueryEnumerator<MapSectorComponent, TransformComponent>();
        while (sectorQuery.MoveNext(out var uid, out var sector, out var xform))
        {
            if (xform.MapID != coords.MapId)
                continue;

            var localPos = Vector2.Transform(worldPos, _transform.GetInvWorldMatrix(uid));
            if (sector.Bounds.Contains(localPos))
                return uid;
        }

        return null;
    }

    private bool HasSpawnLocation(IReadOnlyList<string> tags)
    {
        return GetSpawnLocation(tags, null) != MapCoordinates.Nullspace;
    }

    private MapCoordinates GetSpawnLocation(
        IReadOnlyList<string> tags,
        EntityUid? sectorUid,
        MapCoordinates? relativeTo = null)
    {
        var query = EntityQueryEnumerator<DirectorSpawnPointComponent, TransformComponent>();
        var candidates = new List<(MapCoordinates Coords, float Distance)>();

        while (query.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (!SpawnPointMatches(spawnPoint, tags))
                continue;

            var mapCoords = _transform.GetMapCoordinates(uid, xform);

            // Once the scene anchor is known, related entry/exit markers must resolve on that same map.
            if (relativeTo != null && mapCoords.MapId != relativeTo.Value.MapId)
                continue;

            // If a sector is specified, the spawn point must remain inside that same sector.
            if (sectorUid != null)
            {
                var currentSector = GetSectorForCoordinates(mapCoords);
                if (currentSector != sectorUid)
                    continue;
            }

            var dist = 0f;
            if (relativeTo != null)
                dist = (mapCoords.Position - relativeTo.Value.Position).Length();

            candidates.Add((mapCoords, dist));
        }

        if (candidates.Count == 0)
            return MapCoordinates.Nullspace;

        if (relativeTo != null)
        {
            var sorted = candidates.OrderBy(c => c.Distance).ToList();
            var count = Math.Min(3, sorted.Count);
            return sorted[_random.Next(count)].Coords;
        }

        return _random.Pick(candidates).Coords;
    }

    private EntityCoordinates ToNavigationCoordinates(EntityUid navigator, MapCoordinates target)
    {
        var xform = Transform(navigator);

        if (xform.GridUid is { } gridUid)
            return _transform.ToCoordinates(gridUid, target);

        return _transform.ToCoordinates(target);
    }

    private static bool SpawnPointMatches(DirectorSpawnPointComponent spawnPoint, IReadOnlyList<string> requiredTags)
    {
        if (requiredTags.Count == 0)
            return true;

        foreach (var required in requiredTags)
        {
            if (string.Equals(spawnPoint.LocationTag, required, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var actualTag in spawnPoint.LocationTags)
            {
                if (string.Equals(actualTag, required, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
