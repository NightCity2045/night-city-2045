using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to the physical body of a netrunner during a Hot Sim session.
///     Blocks movement and interaction without using the combat Stun system.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ImmersedBodyComponent : Component
{
}
