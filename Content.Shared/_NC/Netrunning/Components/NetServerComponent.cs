using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Attached to the physical server object in the real world.
///     Acts as the anchor and controller for a Local Network grid.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetServerComponent : Component
{
    /// <summary>
    ///     Maximum number of digital modules (rooms) this server can support.
    /// </summary>
    [DataField("maxModules"), ViewVariables(VVAccess.ReadWrite)]
    public int MaxModules = 1;

    /// <summary>
    ///     Reference to the MapId of the local network grid.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? DigitalGrid;

    /// <summary>
    ///     List of digital node entities spawned in the local net.
    /// </summary>
    public List<EntityUid> SpawnedNodes = new();
}

/// <summary>
///     Attached to digital nodes in the net-grid.
///     Links the digital representation back to the physical device (door, camera, etc).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NetDeviceNodeComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid PhysicalDevice;
}
