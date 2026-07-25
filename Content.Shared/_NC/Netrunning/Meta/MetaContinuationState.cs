using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._NC.Netrunning.Meta;

/// <summary>
/// Represents a single stack frame in the META VM's explicit call stack.
/// Each frame tracks a block of instructions and the current position within it.
/// </summary>
[Serializable, NetSerializable]
public sealed class MetaCallFrame
{
    /// <summary>
    /// The list of instructions in this block (could be top-level, loop body, if-body, etc.).
    /// </summary>
    public readonly List<MetaInstruction> Code;

    /// <summary>
    /// Current instruction pointer within this block. Execution resumes from this index.
    /// </summary>
    public int InstructionPointer;

    /// <summary>
    /// For WHILE/FOR frames: the loop condition expression (null if this is not a loop frame).
    /// </summary>
    public MetaExpression? LoopCondition;

    /// <summary>
    /// For FOR frames: the step instruction executed at the end of each iteration.
    /// </summary>
    public MetaInstruction? LoopStep;

    /// <summary>
    /// Distinguishes the kind of frame for correct resume behavior.
    /// </summary>
    public MetaFrameKind Kind;

    public MetaCallFrame(List<MetaInstruction> code, MetaFrameKind kind)
    {
        Code = code;
        Kind = kind;
        InstructionPointer = 0;
    }
}

/// <summary>
/// Identifies the type of call frame for correct YIELD-resume behavior.
/// </summary>
[Serializable, NetSerializable]
public enum MetaFrameKind : byte
{
    /// <summary>Top-level program body or IF/ELSE body.</summary>
    Block,

    /// <summary>WHILE loop body — re-evaluate condition before each iteration.</summary>
    WhileLoop,

    /// <summary>FOR loop body — run step + re-evaluate condition before each iteration.</summary>
    ForLoop,
}

/// <summary>
/// Full snapshot of a META VM execution that can be saved and restored across ticks.
/// When a YIELD is hit, the VM saves its state here. On the next tick (after the
/// yield delay expires), the scheduler feeds this state back into the VM to continue.
/// </summary>
[Serializable, NetSerializable]
public sealed class MetaContinuationState
{
    /// <summary>
    /// The NetEntity of the cyberdeck running this program.
    /// </summary>
    public NetEntity DeckUid;

    /// <summary>
    /// The NetEntity of the DataShard containing the bytecode.
    /// </summary>
    public NetEntity ShardUid;

    /// <summary>
    /// The NetEntity of the user performing the netrunning.
    /// </summary>
    public NetEntity UserUid;

    /// <summary>
    /// Source of the event that woke a defensive daemon.
    /// Kept in the continuation so event SYS calls remain valid after YIELD.
    /// </summary>
    public NetEntity EventSourceUid;

    /// <summary>
    /// Explicit call stack.
    /// </summary>
    public readonly Stack<MetaCallFrame> CallStack = new();

    // --- Variable stores ---
    public readonly Dictionary<string, int> IntVars = new();
    public readonly Dictionary<string, string> StrVars = new();
    public readonly Dictionary<string, NetEntity?> PtrVars = new();
    public readonly Dictionary<string, MetaArrayValue> ArrVars = new();

    // --- Execution counters ---
    public int GasRemaining;
    public int ReservedRam;
    public int OperationsThisSlice;
    public int SystemCallsThisSlice;
    public bool SchedulerPreemptionRequested;
    public bool RequiresTarget;

    // --- Flow control ---
    public bool Exited;
    public int ExitCode;
    public bool BreakRequested;
    public bool ContinueRequested;
    public string? Error;
    public MetaExecutionFailure Failure;
    public MetaSuspensionReason SuspensionReason;
    public NetEntity AwaitedIntrusionServer;
    public int AwaitedIntrusionId;
    public NetEntity DefenseClearedTarget;

    // --- YIELD timing ---

    /// <summary>
    /// Delay requested by the last YIELD in milliseconds.
    /// Scheduler converts this into an absolute resume time or a DoAfter duration.
    /// </summary>
    public int YieldDelayMs;

    /// <summary>
    /// Absolute server game-time (in seconds) when this continuation may resume.
    /// Used for fallback scheduling when a DoAfter is not created.
    /// </summary>
    public double ResumeAtTime;
    
    /// <summary>
    /// Link to an active progress bar index.
    /// </summary>
    public ushort? DoAfterIndex;

    /// <summary>
    /// Total gas budget this process was started with (for result reporting).
    /// </summary>
    public int InitialGas;

    public bool ShouldStop => Exited || Error != null || SuspensionReason != MetaSuspensionReason.None;
}
