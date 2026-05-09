using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
/// Component for AR glasses or helmets that allow seeing the Net and opening Cyberdeck UI.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetvisorComponent : Component
{
}
