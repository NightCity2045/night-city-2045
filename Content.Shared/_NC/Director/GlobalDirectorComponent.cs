using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._NC.Director;

/// <summary>
/// Component for the global manager of the Living World system.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GlobalDirectorComponent : Component
{
    /// <summary>
    /// When the director will next attempt to start an event.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCheckTime;

    /// <summary>
    /// Minimum delay between events.
    /// </summary>
    [DataField]
    public TimeSpan MinDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum delay between events.
    /// </summary>
    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether the director is currently active.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Minimum connected player count required before the scheduler starts any event.
    /// </summary>
    [DataField]
    public int MinPlayers = 0;

    /// <summary>
    /// Maximum number of concurrently active director events.
    /// </summary>
    [DataField]
    public int MaxConcurrentEvents = 1;

    /// <summary>
    /// Delay from round start before the scheduler becomes active.
    /// </summary>
    [DataField]
    public TimeSpan RoundStartDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Color for phase announcements if not overridden by the event.
    /// </summary>
    [DataField]
    public Color AnnouncementColor = Color.Cyan;

    /// <summary>
    /// Default announcer ID if not specified in the event.
    /// </summary>
    [DataField]
    public string DefaultAnnouncerId = "Director";

    /// <summary>
    /// Runtime history of the last successful start time for each prototype.
    /// Stored on the rule entity so it resets with the round.
    /// </summary>
    public Dictionary<string, TimeSpan> LastRunAt = new();

    /// <summary>
    /// Runtime occurrence counter for each prototype during the round.
    /// </summary>
    public Dictionary<string, int> RunCounts = new();
}
