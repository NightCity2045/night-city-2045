using Robust.Shared.GameStates;

namespace Content.Shared._NC.World.Components;

/// <summary>
/// Marks city infrastructure that must stay where mappers placed it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NCFixedWorldObjectComponent : Component
{
}
