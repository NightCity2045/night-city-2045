namespace Content.Shared._NC.Director;

/// <summary>
/// Marker component for locations where Director events can spawn entities.
/// </summary>
[RegisterComponent]
public sealed partial class DirectorSpawnPointComponent : Component
{
    /// <summary>
    /// Legacy single tag used to categorize this spawn point.
    /// </summary>
    [DataField("locationTag")]
    public string? LocationTag;

    /// <summary>
    /// One or more tags that may be referenced by director phases.
    /// This allows one mapping marker to serve multiple scenario templates.
    /// </summary>
    [DataField("locationTags")]
    public List<string> LocationTags = new();
}
