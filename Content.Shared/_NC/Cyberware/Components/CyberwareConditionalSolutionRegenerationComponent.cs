using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._NC.Cyberware.Components;

/// <summary>
///     Restricts cyberware-driven solution regeneration until the host has enough damage.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberwareConditionalSolutionRegenerationComponent : Component
{
    /// <summary>
    ///     Minimum total host damage required before the implant injects its generated solution.
    /// </summary>
    [DataField("minimumTotalDamage"), AutoNetworkedField]
    public FixedPoint2 MinimumTotalDamage = 1;
}
