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
    ///     Total processing budget available for persistent NET rooms, ICE, demons, and hosted scripts.
    /// </summary>
    [DataField("maxLoad"), ViewVariables(VVAccess.ReadWrite)]
    public int MaxLoad = 100;

    /// <summary>
    ///     Minimum META root strength required to gain server administration.
    /// </summary>
    [DataField("rootDifficulty"), ViewVariables(VVAccess.ReadWrite)]
    public int RootDifficulty = 50;

    /// <summary>
    ///     Processing budget currently reserved by persistent NET architecture.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int UsedLoad;

    /// <summary>
    ///     Reference to the MapId of the local network grid.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? DigitalGrid;

    /// <summary>
    ///     List of digital node entities spawned in the local net.
    /// </summary>
    public List<EntityUid> SpawnedNodes = new();

    /// <summary>
    ///     Persistent module grids docked to this server.
    /// </summary>
    public List<EntityUid> SpawnedModules = new();

    /// <summary>
    ///     Persistent ICE, Black ICE, and demons hosted by this server.
    /// </summary>
    public List<EntityUid> SpawnedDefenses = new();
}

[Serializable, NetSerializable]
public enum NetDeviceNodeKind : byte
{
    Generic,
    Door,
    CameraGroup,
    DataGate,
}

[Serializable, NetSerializable]
public enum NetNodeUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class NetNodeUiState : BoundUserInterfaceState
{
    public readonly NetEntity PhysicalDevice;
    public readonly string DeviceName;
    public readonly NetDeviceNodeKind Kind;
    public readonly int DeviceCount;

    public NetNodeUiState(NetEntity physicalDevice, string deviceName, NetDeviceNodeKind kind = NetDeviceNodeKind.Generic, int deviceCount = 1)
    {
        PhysicalDevice = physicalDevice;
        DeviceName = deviceName;
        Kind = kind;
        DeviceCount = deviceCount;
    }
}

[Serializable, NetSerializable]
public sealed class NetNodeControlMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public NetNodeControlMessage(string action) => Action = action;
}

/// <summary>
///     Attached to digital nodes in the net-grid.
///     Links the digital representation back to the physical device (door, camera, etc).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetDeviceNodeComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, DataField]
    public EntityUid PhysicalDevice;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, DataField]
    public NetDeviceNodeKind Kind = NetDeviceNodeKind.Generic;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, DataField]
    public List<EntityUid> PhysicalDevices = new();

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, DataField]
    public EntityUid? Server;
}
