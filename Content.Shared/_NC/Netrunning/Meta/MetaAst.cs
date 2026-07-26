using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Meta;

[Serializable, NetSerializable]
public enum MetaValueType : byte
{
    Int,
    Str,
    Ptr,
    Arr,
}

[Serializable, NetSerializable]
public enum MetaBinaryOp : byte
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    And,
    Or,
    Equals,
    NotEquals,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

[Serializable, NetSerializable]
public abstract record MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaIntLiteral(int Value) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaStringLiteral(string Value) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaVariableExpression(string Name) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaArrayIndexExpression(string ArrayName, MetaExpression Index) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaUnaryExpression(MetaUnaryOp Op, MetaExpression Operand) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaBinaryExpression(MetaExpression Left, MetaBinaryOp Op, MetaExpression Right) : MetaExpression;

[Serializable, NetSerializable]
public sealed record MetaSysCallExpression(string Name, List<MetaExpression> Arguments) : MetaExpression;

[Serializable, NetSerializable]
public abstract record MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaDefIntInstruction(string Name, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaDefStrInstruction(string Name, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaDefPtrInstruction(string Name, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaDefArrInstruction(string Name, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaAssignInstruction(string Name, MetaAssignOp Op, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaAssignArrayInstruction(string ArrayName, MetaExpression Index, MetaAssignOp Op, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaYieldInstruction(int Milliseconds) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaBreakInstruction() : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaContinueInstruction() : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaExitInstruction(MetaExpression Code) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaSysLogInstruction(MetaExpression Message) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaSysInjectInstruction(MetaExpression Target, MetaExpression Damage) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaSysOverrideInstruction(MetaExpression Target, MetaExpression Key, MetaExpression Value) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaSysSimpleInstruction(string Name, List<MetaExpression> Arguments) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaOnEventInstruction(string EventName, List<MetaInstruction> Body) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaIfInstruction(MetaExpression Condition, List<MetaInstruction> ThenBody, List<MetaInstruction>? ElseBody) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaWhileInstruction(MetaExpression Condition, List<MetaInstruction> Body) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaForInstruction(MetaInstruction? Init, MetaExpression? Condition, MetaInstruction? Step, List<MetaInstruction> Body) : MetaInstruction;

[Serializable, NetSerializable]
public enum MetaAssignOp : byte
{
    Set,
    AddAssign,
    SubAssign,
}

[Serializable, NetSerializable]
public enum MetaUnaryOp : byte
{
    Negate,
    Not,
}

[Serializable, NetSerializable]
public enum MetaProgramKind : byte
{
    Standard,
    DaemonDefensive
}

[Serializable, NetSerializable]
public enum MetaProgramRuntimeState : byte
{
    Ready,
    Running
}

[Serializable, NetSerializable]
public enum MetaExecutionFailure : byte
{
    None,
    Rejected,
    RuntimeError,
    GasExhausted,
    Overheated
}

[Serializable, NetSerializable]
public enum MetaSuspensionReason : byte
{
    None,
    Yield,
    SchedulerPreemption,
    DefenseResponse
}

[Serializable, NetSerializable]
public readonly record struct MetaIntrusionWait(NetEntity Server, int Id);

[Serializable, NetSerializable]
public enum MetaIntrusionOperationKind : byte
{
    Inject,
    Breach,
    Program,
    Immersion,
    Admin,
    Encounter
}

[Serializable, NetSerializable]
public sealed record MetaBytecode(
    List<MetaInstruction> Instructions,
    int RequiredRam,
    MetaProgramKind Kind,
    bool RequiresTarget);

[Serializable, NetSerializable]
public sealed class MetaArrayValue
{
    public MetaValueType ElementType;
    public List<int> IntValues = new();
    public List<string> StrValues = new();
    public List<NetEntity> PtrValues = new();

    public int Count => ElementType switch
    {
        MetaValueType.Str => StrValues.Count,
        MetaValueType.Ptr => PtrValues.Count,
        _ => IntValues.Count
    };
}

[Serializable, NetSerializable]
public sealed record MetaExecutionResult(
    bool Completed,
    bool Yielded,
    string? FatalError,
    MetaExecutionFailure Failure,
    int GasSpent,
    int OperationsThisSlice,
    int SystemCallsThisSlice,
    MetaSuspensionReason SuspensionReason,
    int ReservedRam,
    NetEntity ShardUid_Internal = default);

public interface IMetaRuntimeApi
{
    EntityUid? GetTarget(EntityUid deckUid);
    EntityUid? GetServer(EntityUid deckUid);
    EntityUid GetSelf(EntityUid deckUid);
    int GetIce(EntityUid target);
    IReadOnlyList<EntityUid> GetConnected(EntityUid target);
    string GetClass(EntityUid target);
    MetaIntrusionWait? Inject(EntityUid attacker, EntityUid target, int damage, bool bypassDefense = false);
    int GetTrace(EntityUid deckUid);
    void Cloak(EntityUid deckUid, int strength);
    bool Override(EntityUid target, string key, int value);
    void Ping(EntityUid target);
    EntityUid? GetIntruder(EntityUid deckUid);
    void BurnNeuroport(EntityUid target, int damage);
    void Disconnect(EntityUid target);
    bool IsValid(EntityUid target);
    EntityUid? GetEventSource(EntityUid deckUid);
    EntityUid? FindNearest(EntityUid deckUid, string className, int radius);
    IReadOnlyList<string> GetFiles(EntityUid target);
    bool Download(EntityUid deckUid, EntityUid target, string fileId);
    bool Upload(EntityUid deckUid, EntityUid target, string fileId);
    void MetaLog(EntityUid deckUid, string text);
    IReadOnlyList<int> GetVitals(EntityUid target);
    void SetUser(EntityUid deckUid, EntityUid? userUid);
    void SetEventSource(EntityUid hostUid, EntityUid? source);
    MetaIntrusionWait? Breach(EntityUid attacker, EntityUid target, bool bypassDefense = false);
    bool HasRoot(EntityUid deckUid, EntityUid serverUid);
    bool IsNetworkAdmin(EntityUid hostUid, EntityUid subjectUid);
    bool IsProgramOwner(EntityUid hostUid, EntityUid subjectUid);
    bool TryRoot(EntityUid deckUid, EntityUid serverUid, int strength);
    EntityUid? SpawnIce(EntityUid deckUid, EntityUid shardUid, EntityUid anchor, int strength, bool blackIce);
    EntityUid? SpawnDemon(EntityUid deckUid, EntityUid shardUid, EntityUid anchor, int strength);
    EntityUid? SpawnWall(EntityUid deckUid, EntityUid shardUid, EntityUid anchor, int offsetX, int offsetY);
    EntityUid? SpawnTrap(EntityUid deckUid, EntityUid shardUid, EntityUid anchor, int offsetX, int offsetY);
    void SetWallAllowOwner(EntityUid deckUid, EntityUid wallUid, bool enabled);
    void SetWallAllowNetworkAdmins(EntityUid deckUid, EntityUid wallUid, bool enabled);
    void DemonFollow(EntityUid hostUid, EntityUid demonUid, EntityUid targetUid);
    void DemonStop(EntityUid hostUid, EntityUid demonUid);
    int IsInRange(EntityUid sourceUid, EntityUid targetUid, int tiles);
    void StunAvatar(EntityUid target, int milliseconds);
    void ApplyNeuralDamage(EntityUid target, int damage);
}
