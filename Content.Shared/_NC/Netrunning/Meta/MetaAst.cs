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
public sealed record MetaAllocRamInstruction(int Amount) : MetaInstruction;

[Serializable, NetSerializable]
public sealed record MetaFreeRamInstruction(int Amount) : MetaInstruction;

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
public sealed record MetaBytecode(
    List<MetaInstruction> Instructions,
    int RequiredRam,
    MetaProgramKind Kind);

[Serializable, NetSerializable]
public sealed record MetaExecutionResult(
    bool Completed,
    bool Yielded,
    string? FatalError,
    int GasSpent,
    int LeakedRam,
    NetEntity ShardUid_Internal = default);

public interface IMetaRuntimeApi
{
    EntityUid? GetTarget(EntityUid deckUid);
    EntityUid GetSelf(EntityUid deckUid);
    int GetIce(EntityUid target);
    IReadOnlyList<EntityUid> GetConnected(EntityUid target);
    string GetClass(EntityUid target);
    bool Inject(EntityUid attacker, EntityUid target, int damage);
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
    string InterceptPda(EntityUid target);
    void SetUser(EntityUid deckUid, EntityUid? userUid);
    void SetEventSource(EntityUid hostUid, EntityUid? source);
    bool Breach(EntityUid attacker, EntityUid target);
}
