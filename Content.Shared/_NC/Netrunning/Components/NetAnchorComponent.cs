using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to an entity (usually a door) that serves as a snapping point
///     for connecting new network modules.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetAnchorComponent : Component
{
    /// <summary>
    ///     Is this anchor already connected to another module?
    /// </summary>
    [DataField]
    public bool Connected = false;

    /// <summary>
    ///     The direction this anchor faces (where the next module will be attached).
    /// </summary>
    [DataField]
    public Direction Direction = Direction.North;
}
