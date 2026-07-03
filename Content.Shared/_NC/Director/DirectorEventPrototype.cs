using System.IO;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._NC.Director;

/// <summary>
/// A prototype for an event managed by the Global Director.
/// </summary>
[Prototype("directorEvent")]
public sealed partial class DirectorEventPrototype : IPrototype, IInheritingPrototype, ISerializationHooks
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DirectorEventPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Name { get; private set; } = string.Empty;

    [DataField]
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// The ID of the phase to start with.
    /// </summary>
    [DataField]
    public string StartPhase { get; private set; } = "Start";

    /// <summary>
    /// ID of the announcer to use for phase announcements.
    /// If null, the director's default is used.
    /// </summary>
    [DataField]
    public string? AnnouncerId;

    /// <summary>
    /// Color for phase announcements.
    /// If null, the director's default is used.
    /// </summary>
    [DataField]
    public Color? AnnouncementColor;

    /// <summary>
    /// Weight of this event for the random picker.
    /// </summary>
    [DataField]
    public float Weight { get; private set; } = 10f;

    /// <summary>
    /// Minimum connected player count required for this event to start.
    /// </summary>
    [DataField]
    public int MinPlayers { get; private set; }

    /// <summary>
    /// Maximum connected player count allowed for this event to start.
    /// If null, there is no upper bound.
    /// </summary>
    [DataField]
    public int? MaxPlayers { get; private set; }

    /// <summary>
    /// Minimum round duration before the event becomes eligible.
    /// </summary>
    [DataField]
    public TimeSpan MinRoundDuration { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Cooldown after a successful start before this prototype can be picked again.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Maximum number of times this event may start during a round.
    /// Zero or less means unlimited.
    /// </summary>
    [DataField]
    public int MaxOccurrences { get; private set; }

    /// <summary>
    /// Maximum number of simultaneous instances of this prototype.
    /// </summary>
    [DataField]
    public int MaxSimultaneous { get; private set; } = 1;

    /// <summary>
    /// If true, all spawned entities still owned by this event are deleted when the event ends.
    /// </summary>
    [DataField]
    public bool CleanupOnEnd { get; private set; } = true;

    [DataField]
    public Dictionary<string, DirectorPhase> Phases { get; private set; } = new();

    void ISerializationHooks.AfterDeserialization()
    {
        if (string.IsNullOrWhiteSpace(ID))
            throw new InvalidDataException("Director event has an empty prototype id.");

        if (MinPlayers < 0)
            throw new InvalidDataException($"Director event {ID} has MinPlayers below zero.");

        if (MaxPlayers is { } maxPlayers && maxPlayers < MinPlayers)
            throw new InvalidDataException($"Director event {ID} has MaxPlayers lower than MinPlayers.");

        if (Weight < 0)
            throw new InvalidDataException($"Director event {ID} has a negative weight.");

        if (MaxSimultaneous <= 0)
            throw new InvalidDataException($"Director event {ID} must allow at least one simultaneous instance.");

        if (Phases.Count == 0)
            throw new InvalidDataException($"Director event {ID} does not define any phases.");

        if (!Phases.ContainsKey(StartPhase))
            throw new InvalidDataException($"Director event {ID} references missing start phase '{StartPhase}'.");

        var knownGroupTags = new HashSet<string>();

        foreach (var (phaseId, phase) in Phases)
        {
            foreach (var spawn in phase.Spawns)
            {
                if (string.IsNullOrWhiteSpace(spawn.Prototype))
                {
                    throw new InvalidDataException(
                        $"Director event {ID} phase {phaseId} has a spawn group without a prototype.");
                }

                if (spawn.Amount <= 0)
                {
                    throw new InvalidDataException(
                        $"Director event {ID} phase {phaseId} has spawn group '{spawn.Prototype}' with non-positive amount.");
                }

                if (!string.IsNullOrWhiteSpace(spawn.GroupTag))
                    knownGroupTags.Add(spawn.GroupTag);
            }

            foreach (var trigger in phase.Triggers)
            {
                trigger.NormalizeLegacyFields();

                if (trigger.Type == DirectorTriggerType.None)
                    throw new InvalidDataException($"Director event {ID} phase {phaseId} contains a trigger with type None.");

                if (trigger.Count <= 0)
                    throw new InvalidDataException($"Director event {ID} phase {phaseId} contains a trigger with non-positive count.");
            }

            foreach (var nextPhase in phase.NextPhases.Keys)
            {
                if (!Phases.ContainsKey(nextPhase))
                    throw new InvalidDataException($"Director event {ID} phase {phaseId} references missing next phase '{nextPhase}'.");
            }

            if (phase.ExternalAggressionPhase != null && !Phases.ContainsKey(phase.ExternalAggressionPhase))
            {
                throw new InvalidDataException(
                    $"Director event {ID} phase {phaseId} references missing externalAggressionPhase '{phase.ExternalAggressionPhase}'.");
            }
        }

        foreach (var (phaseId, phase) in Phases)
        {
            foreach (var groupTag in phase.FactionOverrides.Keys)
            {
                if (!knownGroupTags.Contains(groupTag))
                {
                    throw new InvalidDataException(
                        $"Director event {ID} phase {phaseId} overrides unknown group tag '{groupTag}'.");
                }
            }
        }
    }
}

[DataDefinition]
public sealed partial class DirectorPhase
{
    [DataField]
    public string Name { get; private set; } = "Unnamed Phase";

    /// <summary>
    /// How long this phase lasts before automatically progressing.
    /// If null, the phase will not progress automatically by time.
    /// </summary>
    [DataField]
    public TimeSpan? Duration;

    /// <summary>
    /// Locale string for the announcement when this phase starts.
    /// </summary>
    [DataField]
    public string? Announcement;

    /// <summary>
    /// One or more acceptable tags for the scene anchor point.
    /// </summary>
    [DataField("meetLocationTags")]
    public List<string> MeetLocationTags { get; private set; } = new();

    /// <summary>
    /// One or more acceptable tags for the entry spawn point.
    /// </summary>
    [DataField("entryLocationTags")]
    public List<string> EntryLocationTags { get; private set; } = new();

    /// <summary>
    /// HTN Domain to apply to all spawned entities at the start of this phase.
    /// Shared code stores this as a string because HTN prototypes are server-only.
    /// </summary>
    [DataField]
    public string? AiDomain;

    /// <summary>
    /// Optional emergency branch taken when an external attacker disrupts the scene during this phase.
    /// This keeps escalation data-authored instead of hardcoding per-event behavior in the director system.
    /// </summary>
    [DataField]
    public string? ExternalAggressionPhase;

    /// <summary>
    /// If true, all spawned entities will be deleted at the END of this phase.
    /// </summary>
    [DataField]
    public bool Cleanup;

    /// <summary>
    /// Entities to spawn at the start of this phase.
    /// </summary>
    [DataField]
    public List<DirectorSpawnGroup> Spawns { get; private set; } = new();

    /// <summary>
    /// List of triggers that can advance this phase.
    /// </summary>
    [DataField]
    public List<DirectorTrigger> Triggers { get; private set; } = new();

    /// <summary>
    /// Possible next phases with their respective weights.
    /// Key is the phase ID from the prototype's Phases dictionary.
    /// </summary>
    [DataField]
    public Dictionary<string, float> NextPhases { get; private set; } = new();

    /// <summary>
    /// Faction IDs to apply to specific groups at the start of this phase.
    /// Key is the GroupTag defined in DirectorSpawnGroup.
    /// </summary>
    [DataField]
    public Dictionary<string, ProtoId<NpcFactionPrototype>> FactionOverrides { get; private set; } = new();

}

[DataDefinition]
public sealed partial class DirectorSpawnGroup
{
    /// <summary>
    /// Prototype ID of the entity to spawn.
    /// </summary>
    [DataField("prototype", required: true)]
    public EntProtoId Prototype { get; private set; } = string.Empty;

    /// <summary>
    /// Optional tag to identify this group for faction overrides or trigger filters.
    /// </summary>
    [DataField]
    public string? GroupTag;

    /// <summary>
    /// One or more acceptable entry tags for this specific group.
    /// If omitted, the phase-level entry tags are used, then the phase anchor as final fallback.
    /// </summary>
    [DataField("entryLocationTags")]
    public List<string> EntryLocationTags { get; private set; } = new();

    /// <summary>
    /// One or more acceptable retreat tags for this specific group.
    /// Used when the event enters a cleanup / extraction beat.
    /// </summary>
    [DataField("exitLocationTags")]
    public List<string> ExitLocationTags { get; private set; } = new();

    /// <summary>
    /// Faction ID to assign to the spawned entity.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? Faction;

    /// <summary>
    /// Number of entities of this prototype to spawn.
    /// </summary>
    [DataField]
    public int Amount = 1;

}

[DataDefinition]
public sealed partial class DirectorTrigger
{
    [DataField]
    public DirectorTriggerType Type { get; private set; } = DirectorTriggerType.None;

    /// <summary>
    /// Legacy prototype filter kept for compatibility with older YAML.
    /// </summary>
    [DataField("target")]
    public EntProtoId? Target { get; private set; }

    /// <summary>
    /// Optional prototype filter for the trigger.
    /// </summary>
    [DataField]
    public EntProtoId? TargetPrototype { get; private set; }

    /// <summary>
    /// Optional group tag filter for the trigger.
    /// </summary>
    [DataField]
    public string? TargetGroup { get; private set; }

    /// <summary>
    /// Number of occurrences required to activate the trigger.
    /// </summary>
    [DataField]
    public int Count { get; private set; } = 1;

    public void NormalizeLegacyFields()
    {
        TargetPrototype ??= Target;
    }
}

public enum DirectorTriggerType : byte
{
    None = 0,

    /// <summary>
    /// Advances when a qualifying spawned entity from the current phase dies.
    /// </summary>
    MobKilled,

    /// <summary>
    /// Advances when a qualifying spawned entity from the current phase is destroyed.
    /// </summary>
    EntityDestroyed,
}
