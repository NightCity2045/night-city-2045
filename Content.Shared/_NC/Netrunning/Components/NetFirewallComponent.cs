using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to walls surrounding Local Net rooms.
///     Requires a "Breach" operation to be removed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetFirewallComponent : Component
{
    [DataField("difficulty"), ViewVariables(VVAccess.ReadWrite)]
    public int Difficulty = 10;
}
