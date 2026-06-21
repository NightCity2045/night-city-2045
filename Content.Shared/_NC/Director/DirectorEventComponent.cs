using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._NC.Director;

/// <summary>
/// Component attached to an entity representing an active Director event.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DirectorEventComponent : Component
{
    /// <summary>
    /// The current phase ID of the event.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? CurrentPhase;

    /// <summary>
    /// When the current phase is scheduled to end.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan? PhaseEndTime;

    /// <summary>
    /// List of entities spawned by this event.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> SpawnedEntities = new();

    /// <summary>
    /// The ID of the prototype this event was created from.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string PrototypeId = string.Empty;

    /// <summary>
    /// Incremented every time the event enters a phase.
    /// Used to keep trigger handling scoped to the currently active phase.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int PhaseSequence;

    /// <summary>
    /// Counter for triggers in the current phase.
    /// Key is a string representation of the trigger (e.g., "MobKilled:MobHuman").
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> TriggerCounters = new();
}
