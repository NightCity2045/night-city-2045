using Robust.Shared.GameObjects;

namespace Content.Shared._NC.Netrunning.Meta;

/// <summary>
/// Represents a single stack frame in the META VM's explicit call stack.
/// Each frame tracks a block of instructions and the current position within it.
/// </summary>
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
public sealed class MetaContinuationState
{
    /// <summary>
    /// The EntityUid of the cyberdeck running this program.
    /// </summary>
    public EntityUid DeckUid;

    /// <summary>
    /// The EntityUid of the DataShard containing the bytecode (for RAM refund on completion).
    /// </summary>
    public EntityUid ShardUid;

    /// <summary>
    /// Explicit call stack. The bottom frame is the top-level program body.
    /// Loop/if bodies push new frames on top. YIELD saves the entire stack.
    /// </summary>
    public readonly Stack<MetaCallFrame> CallStack = new();

    // --- Variable stores (mirroring VmState) ---
    public readonly Dictionary<string, int> IntVars = new();
    public readonly Dictionary<string, string> StrVars = new();
    public readonly Dictionary<string, EntityUid?> PtrVars = new();
    public readonly Dictionary<string, List<int>> ArrVars = new();

    // --- Execution counters ---
    public int GasRemaining;
    public int AllocatedRam;
    public int FreedRam;

    // --- Flow control ---
    public bool Exited;
    public int ExitCode;
    public bool BreakRequested;
    public bool ContinueRequested;
    public string? Error;

    // --- YIELD timing ---

    /// <summary>
    /// Server game-time (in seconds) when the YIELD delay expires and execution should resume.
    /// </summary>
    public double ResumeAtTime;

    /// <summary>
    /// Total gas budget this process was started with (for result reporting).
    /// </summary>
    public int InitialGas;

    public bool ShouldStop => Exited || Error != null || GasRemaining <= 0;
}
