using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Maths;

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
    /// Load permanently reserved by the installed defensive META program.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int DaemonReservedLoad;

    [DataField("metaGasLimit"), ViewVariables(VVAccess.ReadWrite)]
    public int MetaGasLimit = 5000;

    [DataField("maxConcurrentMetaPrograms"), ViewVariables(VVAccess.ReadWrite)]
    public int MaxConcurrentMetaPrograms = 1;

    [ViewVariables(VVAccess.ReadOnly)]
    public int ActiveMetaPrograms;

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

    /// <summary>
    ///     Runtime topology layout for device nodes, keyed by the physical device identity.
    /// </summary>
    public Dictionary<string, Vector2i> NodeLayout = new();
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
public enum NetServerUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed record NetServerDeviceInfo(NetEntity Uid, string Name, string Class);

[Serializable, NetSerializable]
public sealed record NetTopologyMapEntry(NetEntity Uid, string Name, string Class, Vector2i Tile);

[Serializable, NetSerializable]
public sealed class NetServerScanMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class NetServerConstructMessage : BoundUserInterfaceMessage
{
    public readonly string ModuleId;
    public readonly NetEntity Anchor;

    public NetServerConstructMessage(string moduleId, NetEntity anchor)
    {
        ModuleId = moduleId;
        Anchor = anchor;
    }
}

[Serializable, NetSerializable]
public sealed class NetServerAdminMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class NetServerTopologyMoveMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;
    public readonly Vector2i Tile;

    public NetServerTopologyMoveMessage(NetEntity target, Vector2i tile)
    {
        Target = target;
        Tile = tile;
    }
}

[Serializable, NetSerializable]
public sealed class NetServerUiState : BoundUserInterfaceState
{
    public readonly string ServerName;
    public readonly string ProviderLabel;
    public readonly int UsedLoad;
    public readonly int MaxLoad;
    public readonly int ModuleCount;
    public readonly int ModuleLimit;
    public readonly int ConnectedDeviceCount;
    public readonly bool HasDaemonShard;
    public readonly int DaemonShardCount;
    public readonly int DaemonSlotCount;
    public readonly MetaProgramRuntimeState DaemonRuntimeState;
    public readonly int ActiveMetaPrograms;
    public readonly int MaxConcurrentMetaPrograms;
    public readonly bool HasAdminAccess;
    public readonly bool HasPersistentRoot;
    public readonly bool CanRequestAdmin;
    public readonly string AccessStatus;
    public readonly Vector2i TopologyMinTile;
    public readonly Vector2i TopologyMaxTile;
    public readonly List<NetModuleInfo> AvailableModules;
    public readonly List<NetAnchorInfo> AvailableAnchors;
    public readonly List<NetServerDeviceInfo> ConnectedDevices;
    public readonly List<NetTopologyMapEntry> TopologyEntries;

    public NetServerUiState(
        string serverName,
        string providerLabel,
        int usedLoad,
        int maxLoad,
        int moduleCount,
        int moduleLimit,
        int connectedDeviceCount,
        bool hasDaemonShard,
        int daemonShardCount,
        int daemonSlotCount,
        MetaProgramRuntimeState daemonRuntimeState,
        int activeMetaPrograms,
        int maxConcurrentMetaPrograms,
        bool hasAdminAccess,
        bool hasPersistentRoot,
        bool canRequestAdmin,
        string accessStatus,
        Vector2i topologyMinTile,
        Vector2i topologyMaxTile,
        List<NetModuleInfo> availableModules,
        List<NetAnchorInfo> availableAnchors,
        List<NetServerDeviceInfo> connectedDevices,
        List<NetTopologyMapEntry> topologyEntries)
    {
        ServerName = serverName;
        ProviderLabel = providerLabel;
        UsedLoad = usedLoad;
        MaxLoad = maxLoad;
        ModuleCount = moduleCount;
        ModuleLimit = moduleLimit;
        ConnectedDeviceCount = connectedDeviceCount;
        HasDaemonShard = hasDaemonShard;
        DaemonShardCount = daemonShardCount;
        DaemonSlotCount = daemonSlotCount;
        DaemonRuntimeState = daemonRuntimeState;
        ActiveMetaPrograms = activeMetaPrograms;
        MaxConcurrentMetaPrograms = maxConcurrentMetaPrograms;
        HasAdminAccess = hasAdminAccess;
        HasPersistentRoot = hasPersistentRoot;
        CanRequestAdmin = canRequestAdmin;
        AccessStatus = accessStatus;
        TopologyMinTile = topologyMinTile;
        TopologyMaxTile = topologyMaxTile;
        AvailableModules = availableModules;
        AvailableAnchors = availableAnchors;
        ConnectedDevices = connectedDevices;
        TopologyEntries = topologyEntries;
    }
}

[Serializable, NetSerializable]
public sealed class NetNodeUiState : BoundUserInterfaceState
{
    public readonly NetEntity PhysicalDevice;
    public readonly string DeviceName;
    public readonly NetDeviceNodeKind Kind;
    public readonly int DeviceCount;
    public readonly bool HasLinkedDeck;
    public readonly bool CanManageDefense;
    public readonly int DefenseSlotCount;
    public readonly List<NetNodeShardInfo> AvailableShards;
    public readonly List<NetNodeDefenseInfo> InstalledDefenses;

    public NetNodeUiState(
        NetEntity physicalDevice,
        string deviceName,
        NetDeviceNodeKind kind = NetDeviceNodeKind.Generic,
        int deviceCount = 1,
        bool hasLinkedDeck = false,
        bool canManageDefense = false,
        int defenseSlotCount = 1,
        List<NetNodeShardInfo>? availableShards = null,
        List<NetNodeDefenseInfo>? installedDefenses = null)
    {
        PhysicalDevice = physicalDevice;
        DeviceName = deviceName;
        Kind = kind;
        DeviceCount = deviceCount;
        HasLinkedDeck = hasLinkedDeck;
        CanManageDefense = canManageDefense;
        DefenseSlotCount = defenseSlotCount;
        AvailableShards = availableShards ?? new List<NetNodeShardInfo>();
        InstalledDefenses = installedDefenses ?? new List<NetNodeDefenseInfo>();
    }
}

[Serializable, NetSerializable]
public sealed record NetNodeShardInfo(
    NetEntity Uid,
    string Name,
    int RamCost,
    MetaProgramKind Kind,
    MetaProgramRuntimeState RuntimeState);

[Serializable, NetSerializable]
public sealed record NetNodeDefenseInfo(
    NetEntity Uid,
    string Name,
    MetaProgramRuntimeState RuntimeState);

[Serializable, NetSerializable]
public sealed class NetNodeControlMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public NetNodeControlMessage(string action) => Action = action;
}

[Serializable, NetSerializable]
public sealed class NetNodeExecuteShardMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Shard;
    public NetNodeExecuteShardMessage(NetEntity shard) => Shard = shard;
}

[Serializable, NetSerializable]
public sealed class NetNodeEjectDefenseMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Shard;
    public NetNodeEjectDefenseMessage(NetEntity shard) => Shard = shard;
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

    /// <summary>
    ///     Server-side bookkeeping for live viewers of this node's physical feed.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> ActiveViewers = new();
}
