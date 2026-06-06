using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to an entity that represents a modular room/cluster in a local network.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetModuleComponent : Component
{
    /// <summary>
    ///     The ID of the NetModulePrototype used to create this cluster.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string PrototypeId = string.Empty;

    /// <summary>
    ///     Permanent server processing budget reserved while this module is alive.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ReservedLoad;

    /// <summary>
    ///     Physical server that owns this persistent module.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Server;
}
