using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
/// Component for AR glasses or helmets that allow seeing the Net and opening Cyberdeck UI.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetvisorComponent : Component
{
    /// <summary>
    /// Additional distance (in meters) added to the cyberdeck's base link range.
    /// </summary>
    [DataField("bonusRange"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float BonusRange = 10.0f;
}
