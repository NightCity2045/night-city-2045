using Robust.Shared.GameStates;

namespace Content.Shared._NC.RTS.Components;

/// <summary>
/// Explicitly marks an inanimate entity as a valid target for an RTS attack order.
/// Living mobs remain valid targets without this marker.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RTSAttackableComponent : Component
{
}
