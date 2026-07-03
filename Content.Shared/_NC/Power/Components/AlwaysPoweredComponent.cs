using Robust.Shared.GameStates;

namespace Content.Shared._NC.Power.Components;

/// <summary>
/// Marks a map entity as globally bypassing APC power requirements for devices on that map.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlwaysPoweredComponent : Component
{
}
