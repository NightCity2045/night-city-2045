using Robust.Shared.GameStates;

namespace Content.Shared._NC.CitiNet.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class DataChipComponent : Component
{
    /// <summary>
    /// The ID of the site that this chip unlocks when inserted into a terminal.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("unlockedSiteId")]
    public string? UnlockedSiteId;
}
