using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Selective collision data for a wall materialized by a META program.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetBarrierComponent : Component
{
    [DataField("server"), AutoNetworkedField]
    public EntityUid? Server;

    [DataField("ownerDeck"), AutoNetworkedField]
    public EntityUid? OwnerDeck;

    [DataField("allowOwner"), AutoNetworkedField]
    public bool AllowOwner;

    [DataField("allowNetworkAdmins"), AutoNetworkedField]
    public bool AllowNetworkAdmins;
}
