using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Marks a structure in a local network as a gateway to the Global NET.
///     Entering this gateway triggers geo-routing and teleportation to a regional hub.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetDataGateComponent : Component
{
    /// <summary>
    ///     Visual offset for the "tunnel" effect during transition.
    /// </summary>
    [DataField("transitionDuration")]
    public float TransitionDuration = 2.0f;
}
