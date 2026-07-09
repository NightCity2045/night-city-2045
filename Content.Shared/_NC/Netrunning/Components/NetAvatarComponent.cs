using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.Actions;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to an entity that represents a netrunner's avatar in the digital space.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetAvatarComponent : Component
{
    /// <summary>
    ///     The physical body of the netrunner.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? PhysicalBody;

    /// <summary>
    ///     The deck being used for immersion.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Cyberdeck;
}

public sealed partial class JackOutActionEvent : InstantActionEvent
{
}

public sealed partial class OpenLinkedCyberdeckActionEvent : InstantActionEvent
{
}
