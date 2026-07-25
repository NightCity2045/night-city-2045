using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Damage;

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
    public readonly string Name;
    public readonly MetaProgramKind ProgramKind;
    public readonly NetEntity? TargetShard;

    public CyberdeckCompileMessage(string code, string name, MetaProgramKind programKind, NetEntity? targetShard)
    {
        Code = code;
        Name = name;
        ProgramKind = programKind;
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
public sealed class CyberdeckHotSimMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CyberdeckLogMessage : BoundUserInterfaceMessage
{
    public readonly string Text;
    public CyberdeckLogMessage(string text) => Text = text;
}

[Serializable, NetSerializable]
public sealed class CyberdeckConstructMessage : BoundUserInterfaceMessage
{
    public readonly string ModuleId;
    public readonly NetEntity Anchor;
    public CyberdeckConstructMessage(string moduleId, NetEntity anchor)
    {
        ModuleId = moduleId;
        Anchor = anchor;
    }
}

[Serializable, NetSerializable]
public sealed record NetModuleInfo(string Id, string Name, string Description, int RamCost, int Price);

[Serializable, NetSerializable]
public sealed record NetAnchorInfo(NetEntity Uid, Direction Dir, bool Connected);

[Serializable, NetSerializable]
public sealed class CyberdeckUiState : BoundUserInterfaceState
{
    public readonly int CurrentRam;
    public readonly int ReservedRam;
    public readonly int MaxRam;
    public readonly float RecoverySpeed;
    public readonly int GasLimit;
    public readonly int LastGasSpent;
    public readonly bool LastExecutionRunning;
    public readonly MetaExecutionFailure LastExecutionFailure;
    public readonly float CurrentHeat;
    public readonly float MaxHeat;
    public readonly float CoolingPerSecond;
    public readonly int CurrentTrace;
    public readonly int StorageUsed;
    public readonly int StorageCapacity;
    public readonly int ServerUsedLoad;
    public readonly int ServerMaxLoad;
    public readonly NetEntity? ActiveTarget;
    public readonly NetEntity? ActiveServer;
    public readonly bool HasServerAdminAccess;
    public readonly List<(NetEntity Uid, string Name, string Source, MetaProgramKind Kind, int RamCost, MetaProgramRuntimeState RuntimeState)> Shards;
    public readonly bool HasAR;
    public readonly List<NetModuleInfo> AvailableModules;
    public readonly List<NetAnchorInfo> AvailableAnchors;

    public CyberdeckUiState(
        int currentRam,
        int reservedRam,
        int maxRam,
        float recoverySpeed,
        int gasLimit,
        int lastGasSpent,
        bool lastExecutionRunning,
        MetaExecutionFailure lastExecutionFailure,
        float currentHeat,
        float maxHeat,
        float coolingPerSecond,
        int currentTrace,
        int storageUsed,
        int storageCapacity,
        int serverUsedLoad,
        int serverMaxLoad,
        NetEntity? activeTarget,
        NetEntity? activeServer,
        bool hasServerAdminAccess,
        List<(NetEntity, string, string, MetaProgramKind, int, MetaProgramRuntimeState)> shards,
        bool hasAR,
        List<NetModuleInfo> availableModules,
        List<NetAnchorInfo> availableAnchors)
    {
        CurrentRam = currentRam;
        ReservedRam = reservedRam;
        MaxRam = maxRam;
        RecoverySpeed = recoverySpeed;
        GasLimit = gasLimit;
        LastGasSpent = lastGasSpent;
        LastExecutionRunning = lastExecutionRunning;
        LastExecutionFailure = lastExecutionFailure;
        CurrentHeat = currentHeat;
        MaxHeat = maxHeat;
        CoolingPerSecond = coolingPerSecond;
        CurrentTrace = currentTrace;
        StorageUsed = storageUsed;
        StorageCapacity = storageCapacity;
        ServerUsedLoad = serverUsedLoad;
        ServerMaxLoad = serverMaxLoad;
        ActiveTarget = activeTarget;
        ActiveServer = activeServer;
        HasServerAdminAccess = hasServerAdminAccess;
        Shards = shards;
        HasAR = hasAR;
        AvailableModules = availableModules;
        AvailableAnchors = availableAnchors;
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
    /// RAM held by active and suspended META processes.
    /// </summary>
    [DataField("reservedRam"), ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int ReservedRam;

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
    /// Damage caused by exhausting the instruction budget.
    /// </summary>
    [DataField("gasFailureDamage"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public DamageSpecifier? GasFailureDamage;

    /// <summary>
    /// Base paralysis duration caused by an instruction-budget failure.
    /// </summary>
    [DataField("gasFailureStunDuration"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float GasFailureStunDuration = 1f;

    /// <summary>
    /// Multiplier applied to overload consequences while the user is immersed.
    /// </summary>
    [DataField("hotSimGasFailureMultiplier"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float HotSimGasFailureMultiplier = 2f;

    /// <summary>
    /// Gas consumed by the most recently finished execution.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int LastGasSpent;

    /// <summary>
    /// Whether the last execution reported to telemetry is suspended and waiting to resume.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool LastExecutionRunning;

    /// <summary>
    /// Failure state of the most recently finished execution.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public MetaExecutionFailure LastExecutionFailure;

    [DataField("currentHeat"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float CurrentHeat;

    [DataField("maxHeat"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MaxHeat = 100f;

    [DataField("coolingPerSecond"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float CoolingPerSecond = 5f;

    [DataField("heatPerOperation"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float HeatPerOperation = 0.01f;

    [DataField("heatPerSystemCall"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float HeatPerSystemCall = 1f;

    /// <summary>
    /// Storage capacity in abstract "file units" for DOWNLOAD/UPLOAD commands.
    /// </summary>
    [DataField("storageCapacity"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int StorageCapacity = 5;

    /// <summary>
    /// Current trace level accumulated during hostile network actions.
    /// </summary>
    [DataField("traceLevel"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int TraceLevel;

    /// <summary>
    /// Files currently stored on the deck's persistent storage.
    /// </summary>
    [DataField("storedFiles"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public List<string> StoredFiles = new();

    /// <summary>
    ///     The physical server this deck is currently interacting with.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? ActiveServer;
}
