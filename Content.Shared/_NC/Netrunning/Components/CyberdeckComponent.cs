using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

[Serializable, NetSerializable]
public enum CyberdeckUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CyberdeckCompileMessage : BoundUserInterfaceMessage
{
    public readonly string Code;
    public readonly NetEntity? TargetShard;

    public CyberdeckCompileMessage(string code, NetEntity? targetShard)
    {
        Code = code;
        TargetShard = targetShard;
    }
}

[Serializable, NetSerializable]
public sealed class CyberdeckEjectMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Shard;
    public CyberdeckEjectMessage(NetEntity shard) => Shard = shard;
}

[Serializable, NetSerializable]
public sealed class CyberdeckExecuteMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Shard;
    public CyberdeckExecuteMessage(NetEntity shard) => Shard = shard;
}

[Serializable, NetSerializable]
public sealed class CyberdeckLogMessage : BoundUserInterfaceMessage
{
    public readonly string Text;
    public CyberdeckLogMessage(string text) => Text = text;
}

[Serializable, NetSerializable]
public sealed class CyberdeckUiState : BoundUserInterfaceState
{
    public readonly int CurrentRam;
    public readonly int MaxRam;
    public readonly NetEntity? ActiveTarget;
    public readonly List<(NetEntity Shard, string Name, string Source)> InstalledShards;
    public readonly bool HasNetvisor;

    public CyberdeckUiState(int currentRam, int maxRam, NetEntity? activeTarget, List<(NetEntity, string, string)> installedShards, bool hasNetvisor)
    {
        CurrentRam = currentRam;
        MaxRam = maxRam;
        ActiveTarget = activeTarget;
        InstalledShards = installedShards;
        HasNetvisor = hasNetvisor;
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberdeckComponent : Component
{
    public const string ShardContainerId = "cyberdeck_shards";

    [ViewVariables]
    public List<EntityUid> InstalledShards = new();

    /// <summary>
    /// Maximum number of shards this deck can hold.
    /// </summary>
    [DataField("maxShards"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int MaxShards = 2;
    [DataField("maxRam"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int MaxRam = 10;

    /// <summary>
    /// Current available RAM.
    /// </summary>
    [DataField("currentRam"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int CurrentRam = 10;

    /// <summary>
    /// RAM capacity currently blocked by memory leaks until reboot/cleanup.
    /// </summary>
    [DataField("leakedRam"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int LeakedRam;

    /// <summary>
    /// RAM recovery per second.
    /// </summary>
    [DataField("recoverySpeed"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float RecoverySpeed = 1.0f;

    /// <summary>
    /// Netrunning range (in tiles/meters).
    /// </summary>
    [DataField("range"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Range = 10.0f;

    /// <summary>
    /// The currently selected target for hacks.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ActiveTarget;

    /// <summary>
    /// Color of the visual beam (synced for NetVisor).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color BeamColor = Color.Red;

    /// <summary>
    /// Accumulator for passive RAM regeneration.
    /// </summary>
    public float RecoveryAccumulator = 0f;

    /// <summary>
    /// Radius to scan for nearby network devices.
    /// </summary>
    [DataField("scanRadius"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float ScanRadius = 1.0f; // Default small radius per user request

    /// <summary>
    /// Cache of last scan results. Not serialized.
    /// </summary>
    [ViewVariables]
    public Dictionary<NetEntity, string> LastScan = new();

    /// <summary>
    /// Set of Server UIDs that have been successfully hacked (Root Access).
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> HackedNetworks = new();

    /// <summary>
    /// Maximum instruction cycles (Gas) allowed per script execution.
    /// Street deck: 1000, Corporate: 5000, Military: 25000.
    /// </summary>
    [DataField("gasLimit"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int GasLimit = 1000;

    /// <summary>
    /// Storage capacity in abstract "file units" for DOWNLOAD/UPLOAD commands.
    /// </summary>
    [DataField("storageCapacity"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int StorageCapacity = 5;
}
