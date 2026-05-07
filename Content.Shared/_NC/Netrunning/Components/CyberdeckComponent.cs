using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

[Serializable, NetSerializable]
public enum CyberdeckUiKey : byte
{
    Key
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberdeckComponent : Component
{
    /// <summary>
    /// Maximum RAM capacity of the deck.
    /// </summary>
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
