using Robust.Shared.GameStates;

namespace Content.Shared._NC.CharacterNotes.Components;

/// <summary>
/// Marks profile-backed characters whose displayed identity is resolved per viewer.
/// ProfileId is intentionally not networked; clients only need the marker presence.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NCCharacterIdentityComponent : Component
{
    [DataField]
    public int ProfileId;
}
