using Robust.Shared.GameObjects;

namespace Content.Shared._NC.Netrunning.Meta;

public enum MetaValueType : byte
{
    Int,
    Str,
    Ptr,
    Arr,
}

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

public abstract record MetaExpression;
public sealed record MetaIntLiteral(int Value) : MetaExpression;
public sealed record MetaStringLiteral(string Value) : MetaExpression;
public sealed record MetaVariableExpression(string Name) : MetaExpression;
public sealed record MetaArrayIndexExpression(string ArrayName, MetaExpression Index) : MetaExpression;
public sealed record MetaUnaryExpression(MetaUnaryOp Op, MetaExpression Operand) : MetaExpression;
public sealed record MetaBinaryExpression(MetaExpression Left, MetaBinaryOp Op, MetaExpression Right) : MetaExpression;
public sealed record MetaSysCallExpression(string Name, List<MetaExpression> Arguments) : MetaExpression;

public abstract record MetaInstruction;
public sealed record MetaAllocRamInstruction(int Amount) : MetaInstruction;
public sealed record MetaFreeRamInstruction(int Amount) : MetaInstruction;
public sealed record MetaDefIntInstruction(string Name, MetaExpression Value) : MetaInstruction;
public sealed record MetaDefStrInstruction(string Name, MetaExpression Value) : MetaInstruction;
public sealed record MetaDefPtrInstruction(string Name, MetaExpression Value) : MetaInstruction;
public sealed record MetaDefArrInstruction(string Name, MetaExpression Value) : MetaInstruction;
public sealed record MetaAssignInstruction(string Name, MetaAssignOp Op, MetaExpression Value) : MetaInstruction;
public sealed record MetaAssignArrayInstruction(string ArrayName, MetaExpression Index, MetaAssignOp Op, MetaExpression Value) : MetaInstruction;
public sealed record MetaYieldInstruction(int Milliseconds) : MetaInstruction;
public sealed record MetaBreakInstruction() : MetaInstruction;
public sealed record MetaContinueInstruction() : MetaInstruction;
public sealed record MetaExitInstruction(MetaExpression Code) : MetaInstruction;
public sealed record MetaSysLogInstruction(MetaExpression Message) : MetaInstruction;
public sealed record MetaSysInjectInstruction(MetaExpression Target, MetaExpression Damage) : MetaInstruction;
public sealed record MetaSysOverrideInstruction(MetaExpression Target, MetaExpression Key, MetaExpression Value) : MetaInstruction;
public sealed record MetaSysSimpleInstruction(string Name, List<MetaExpression> Arguments) : MetaInstruction;
public sealed record MetaOnEventInstruction(string EventName, List<MetaInstruction> Body) : MetaInstruction;
public sealed record MetaIfInstruction(MetaExpression Condition, List<MetaInstruction> ThenBody, List<MetaInstruction>? ElseBody) : MetaInstruction;
public sealed record MetaWhileInstruction(MetaExpression Condition, List<MetaInstruction> Body) : MetaInstruction;
public sealed record MetaForInstruction(MetaInstruction? Init, MetaExpression? Condition, MetaInstruction? Step, List<MetaInstruction> Body) : MetaInstruction;

public enum MetaAssignOp : byte
{
    Set,
    AddAssign,
    SubAssign,
}

public enum MetaUnaryOp : byte
{
    Negate,
    Not,
}

public enum MetaProgramKind : byte
{
    Standard,
    DaemonDefensive
}

public sealed record MetaBytecode(
    List<MetaInstruction> Instructions,
    int RequiredRam,
    MetaProgramKind Kind);

public sealed record MetaExecutionResult(
    bool Completed,
    bool Yielded,
    string? FatalError,
    int GasSpent,
    int LeakedRam);

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
}
