namespace Content.Shared._NC.CharacterNotes.Events;

/// <summary>
/// Lets client/server systems override how a target's identity is shown to a specific viewer.
/// </summary>
[ByRefEvent]
public struct NCResolveViewerDisplayNameEvent(EntityUid target, EntityUid viewer, string fallbackName)
{
    public EntityUid Target = target;
    public EntityUid Viewer = viewer;
    public string DisplayName = fallbackName;
}
