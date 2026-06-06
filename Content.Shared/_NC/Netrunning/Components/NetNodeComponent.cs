using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to physical entities (Servers, Decks, Wall Terminals) 
///     that act as an anchor or entry point for a digital network grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetNodeComponent : Component
{
    /// <summary>
    ///     The digital grid associated with this node.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? DigitalGrid;

    /// <summary>
    ///     Unique offset index in the Net Map to prevent grid overlap.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ZoneIndex = -1;

    /// <summary>
    ///     If true, the grid will be deleted when the last user jacks out.
    ///     Used for cyberdeck-only subnets.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Ephemeral = false;
}
