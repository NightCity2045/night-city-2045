using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
/// The META Virtual Machine. Executes compiled META bytecode on a cyberdeck.
///
/// Key design: uses an explicit call stack (Stack of MetaCallFrame) instead of recursive
/// ExecuteBlock calls. This allows the VM to freeze execution mid-YIELD and resume it
/// on a later tick by saving/restoring the MetaContinuationState.
///
/// Gas system: every instruction and expression evaluation costs gas. When gas hits 0,
/// the VM halts with a fatal error (Dumpshock).
/// </summary>
public sealed class MetaVirtualMachineSystem : EntitySystem
{
    [Dependency] private readonly MetaApiSystem _api = default!;
    public const int DefaultGasPerRun = 1000;

    // ──────────────────────────────────────────────
    //  Public API: first-run execution
    // ──────────────────────────────────────────────

    /// <summary>
    /// Begin executing a META program from scratch. Returns immediately if YIELD is hit.
    /// The caller (MetaProgramSystem) is responsible for saving the continuation state
    /// into ActiveMetaProcessComponent when the result says Yielded=true.
    /// </summary>
    public MetaVmRunResult Execute(EntityUid deckUid, EntityUid shardUid, MetaBytecode bytecode, int gasLimit = DefaultGasPerRun)
    {
        var state = new MetaContinuationState
        {
            DeckUid = deckUid,
            ShardUid = shardUid,
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
        };

        // Push top-level instructions (excluding ON_EVENT handlers) as the first frame.
        var topLevel = bytecode.Instructions.Where(i => i is not MetaOnEventInstruction).ToList();
        state.CallStack.Push(new MetaCallFrame(topLevel, MetaFrameKind.Block));

        return RunUntilYieldOrDone(state);
    }

    /// <summary>
    /// Resume a previously YIELD-suspended program from its saved continuation state.
    /// Called by MetaSchedulerSystem each tick for processes whose delay has expired.
    /// </summary>
    public MetaVmRunResult Resume(MetaContinuationState state)
    {
        return RunUntilYieldOrDone(state);
    }

    /// <summary>
    /// Execute event handlers (ON_EVENT) for defensive ICE daemons. These run synchronously
    /// (no YIELD-resume support for event handlers to keep ICE deterministic).
    /// </summary>
    public MetaExecutionResult ExecuteEvent(EntityUid hostUid, MetaBytecode bytecode, string eventName, EntityUid? eventSource, int gasLimit = DefaultGasPerRun)
    {
        _api.SetEventSource(hostUid, eventSource);

        var handlers = bytecode.Instructions
            .OfType<MetaOnEventInstruction>()
            .Where(h => h.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var state = new MetaContinuationState
        {
            DeckUid = hostUid,
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
        };

        foreach (var h in handlers)
        {
            state.CallStack.Clear();
            state.CallStack.Push(new MetaCallFrame(h.Body, MetaFrameKind.Block));
            RunLoop(state);
            if (state.ShouldStop)
                break;
        }

        _api.SetEventSource(hostUid, null);

        if (state.Error == null && state.GasRemaining <= 0)
            state.Error = "[FATAL] META: Превышен лимит инструкций. Дампшок.";

        return new MetaExecutionResult(
            state.Error == null && !state.Exited,
            false,
            state.Error,
            gasLimit - state.GasRemaining,
            Math.Max(0, state.AllocatedRam - state.FreedRam));
    }

    // ──────────────────────────────────────────────
    //  Core VM loop (explicit stack, no recursion)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Runs the VM until it either completes, hits a YIELD, runs out of gas, or errors.
    /// Returns a result that includes the continuation state if yielded.
    /// </summary>
    private MetaVmRunResult RunUntilYieldOrDone(MetaContinuationState state)
    {
        RunLoop(state);

        // Calculate memory leak.
        var leak = Math.Max(0, state.AllocatedRam - state.FreedRam);
        var yielded = state.CallStack.Count > 0 && state.Error == null && !state.Exited && state.GasRemaining > 0;

        if (!yielded)
        {
            // Program finished (or errored). Apply memory leak to cyberdeck.
            if (TryComp<CyberdeckComponent>(state.DeckUid, out var deck) && leak > 0)
            {
                deck.LeakedRam += leak;
                deck.CurrentRam = Math.Max(0, Math.Min(deck.CurrentRam, deck.MaxRam - deck.LeakedRam));
                Dirty(state.DeckUid, deck);
            }
        }

        if (state.Error == null && state.GasRemaining <= 0)
            state.Error = "[FATAL] META: Превышен лимит инструкций. Дампшок.";

        var execResult = new MetaExecutionResult(
            state.Error == null && !yielded && !state.Exited,
            yielded,
            state.Error,
            state.InitialGas - state.GasRemaining,
            yielded ? 0 : leak);

        return new MetaVmRunResult(execResult, yielded ? state : null);
    }

    /// <summary>
    /// The main non-recursive interpreter loop. Processes frames from the call stack
    /// until the stack is empty (program done), YIELD is hit, or an error/gas-out occurs.
    /// </summary>
    private void RunLoop(MetaContinuationState s)
    {
        while (s.CallStack.Count > 0)
        {
            if (s.ShouldStop)
                return;

            var frame = s.CallStack.Peek();

            // Frame exhausted — pop it and handle loop re-entry.
            if (frame.InstructionPointer >= frame.Code.Count)
            {
                s.CallStack.Pop();

                // For loop frames, re-evaluate condition and potentially re-enter.
                if (frame.Kind == MetaFrameKind.WhileLoop)
                {
                    if (s.BreakRequested) { s.BreakRequested = false; continue; }
                    s.ContinueRequested = false;
                    ConsumeGas(s, 1);
                    if (!s.ShouldStop && frame.LoopCondition != null && EvalInt(s, frame.LoopCondition) != 0)
                    {
                        // Re-push the frame with IP reset to 0 for next iteration.
                        frame.InstructionPointer = 0;
                        s.CallStack.Push(frame);
                    }
                    continue;
                }

                if (frame.Kind == MetaFrameKind.ForLoop)
                {
                    if (s.BreakRequested) { s.BreakRequested = false; continue; }
                    // Execute step instruction (e.g. i += 1).
                    if (frame.LoopStep != null && !s.ShouldStop)
                        ExecuteSingleInstruction(s, frame.LoopStep);
                    s.ContinueRequested = false;
                    ConsumeGas(s, 1);
                    if (!s.ShouldStop && (frame.LoopCondition == null || EvalInt(s, frame.LoopCondition) != 0))
                    {
                        frame.InstructionPointer = 0;
                        s.CallStack.Push(frame);
                    }
                    continue;
                }

                // Regular block frame (top-level, if-body, etc.) — just pop, done.
                continue;
            }

            // Fetch the current instruction and advance IP.
            var instruction = frame.Code[frame.InstructionPointer];
            frame.InstructionPointer++;

            ConsumeGas(s, 1);
            if (s.ShouldStop)
                return;

            // Handle YIELD: save resume time and return to caller without popping the stack.
            if (instruction is MetaYieldInstruction yield)
            {
                // ResumeAtTime will be set by the caller (MetaProgramSystem / Scheduler).
                // We store the requested delay in ms for the caller to convert.
                s.ResumeAtTime = yield.Milliseconds; // Temporarily store ms; caller converts to game-time.
                return; // <-- VM suspends here. Call stack is intact for resume.
            }

            // Handle BREAK / CONTINUE: unwind the stack to the nearest loop frame.
            if (instruction is MetaBreakInstruction)
            {
                UnwindToLoop(s, setBreak: true);
                continue;
            }

            if (instruction is MetaContinueInstruction)
            {
                UnwindToLoop(s, setBreak: false);
                continue;
            }

            // All other instructions.
            ExecuteSingleInstruction(s, instruction);

            // If the instruction pushed new frames (IF/WHILE/FOR), the loop naturally
            // processes them on the next iteration since they're on top of the stack.
        }
    }

    /// <summary>
    /// Unwinds the call stack to the nearest loop frame for BREAK/CONTINUE.
    /// </summary>
    private static void UnwindToLoop(MetaContinuationState s, bool setBreak)
    {
        while (s.CallStack.Count > 0)
        {
            var top = s.CallStack.Peek();
            if (top.Kind is MetaFrameKind.WhileLoop or MetaFrameKind.ForLoop)
            {
                if (setBreak)
                {
                    s.BreakRequested = true;
                    // Force the frame to exhaust so it pops on next iteration.
                    top.InstructionPointer = top.Code.Count;
                }
                else
                {
                    s.ContinueRequested = true;
                    // Jump to end of body so the loop re-evaluates condition.
                    top.InstructionPointer = top.Code.Count;
                }
                return;
            }

            s.CallStack.Pop(); // Pop non-loop frames (if-bodies, etc.)
        }
    }

    // ──────────────────────────────────────────────
    //  Instruction execution (non-recursive, pushes frames onto stack)
    // ──────────────────────────────────────────────

    private void ExecuteSingleInstruction(MetaContinuationState s, MetaInstruction i)
    {
        switch (i)
        {
            case MetaAllocRamInstruction a:
                s.AllocatedRam += Math.Max(0, a.Amount);
                break;
            case MetaFreeRamInstruction f:
                s.FreedRam += Math.Max(0, f.Amount);
                break;
            case MetaDefIntInstruction d:
                s.IntVars[d.Name] = EvalInt(s, d.Value);
                break;
            case MetaDefStrInstruction d:
                s.StrVars[d.Name] = EvalString(s, d.Value);
                break;
            case MetaDefPtrInstruction d:
                s.PtrVars[d.Name] = EvalPtr(s, d.Value);
                break;
            case MetaDefArrInstruction d:
                s.ArrVars[d.Name] = EvalArray(s, d.Value);
                break;
            case MetaAssignInstruction a:
                AssignVar(s, a);
                break;
            case MetaAssignArrayInstruction aa:
                AssignArray(s, aa);
                break;
            case MetaExitInstruction e:
                s.ExitCode = EvalInt(s, e.Code);
                s.Exited = true;
                break;
            case MetaSysLogInstruction log:
                _api.Log(s.DeckUid, EvalString(s, log.Message));
                break;
            case MetaSysInjectInstruction inj:
                var t = EvalPtr(s, inj.Target);
                if (t != null) _api.Inject(s.DeckUid, t.Value, EvalInt(s, inj.Damage));
                break;
            case MetaSysOverrideInstruction ov:
                var ot = EvalPtr(s, ov.Target);
                if (ot != null) _api.Override(ot.Value, EvalString(s, ov.Key), EvalInt(s, ov.Value));
                break;
            case MetaSysSimpleInstruction ss:
                ExecSysSimple(s, ss);
                break;

            // --- Control flow: push new frames onto the call stack ---

            case MetaIfInstruction ifi:
                if (EvalInt(s, ifi.Condition) != 0)
                    s.CallStack.Push(new MetaCallFrame(ifi.ThenBody, MetaFrameKind.Block));
                else if (ifi.ElseBody != null)
                    s.CallStack.Push(new MetaCallFrame(ifi.ElseBody, MetaFrameKind.Block));
                break;

            case MetaWhileInstruction w:
                // Evaluate condition before first entry.
                if (EvalInt(s, w.Condition) != 0)
                {
                    var wFrame = new MetaCallFrame(w.Body, MetaFrameKind.WhileLoop)
                    {
                        LoopCondition = w.Condition,
                    };
                    s.CallStack.Push(wFrame);
                }
                break;

            case MetaForInstruction f:
                // Execute init statement (e.g., DEF INT i = 0).
                if (f.Init != null)
                    ExecuteSingleInstruction(s, f.Init);
                // Evaluate condition before first entry.
                if (!s.ShouldStop && (f.Condition == null || EvalInt(s, f.Condition) != 0))
                {
                    var fFrame = new MetaCallFrame(f.Body, MetaFrameKind.ForLoop)
                    {
                        LoopCondition = f.Condition,
                        LoopStep = f.Step,
                    };
                    s.CallStack.Push(fFrame);
                }
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  Variable assignment helpers
    // ──────────────────────────────────────────────

    private void AssignVar(MetaContinuationState s, MetaAssignInstruction a)
    {
        if (s.IntVars.ContainsKey(a.Name))
        {
            s.IntVars[a.Name] = ApplyAssignOp(a.Op, s.IntVars[a.Name], EvalInt(s, a.Value));
            return;
        }
        if (s.StrVars.ContainsKey(a.Name))
        {
            s.StrVars[a.Name] = EvalString(s, a.Value);
            return;
        }
        if (s.PtrVars.ContainsKey(a.Name))
        {
            s.PtrVars[a.Name] = EvalPtr(s, a.Value);
            return;
        }
        if (s.ArrVars.ContainsKey(a.Name))
        {
            s.ArrVars[a.Name] = EvalArray(s, a.Value);
            return;
        }
        s.Error = $"Undefined variable '{a.Name}'.";
    }

    private void AssignArray(MetaContinuationState s, MetaAssignArrayInstruction a)
    {
        if (!s.ArrVars.TryGetValue(a.ArrayName, out var arr))
        {
            s.Error = $"Array '{a.ArrayName}' is not defined.";
            return;
        }
        var idx = EvalInt(s, a.Index);
        if (idx < 0 || idx >= arr.Count)
        {
            s.Error = $"Array index out of bounds for '{a.ArrayName}'.";
            return;
        }
        var current = arr[idx];
        arr[idx] = ApplyAssignOp(a.Op, current, EvalInt(s, a.Value));
    }

    private static int ApplyAssignOp(MetaAssignOp op, int left, int right)
    {
        return op switch
        {
            MetaAssignOp.Set => right,
            MetaAssignOp.AddAssign => left + right,
            MetaAssignOp.SubAssign => left - right,
            _ => right
        };
    }

    // ──────────────────────────────────────────────
    //  Expression evaluation
    // ──────────────────────────────────────────────

    private int EvalInt(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop) return 0;
        return e switch
        {
            MetaIntLiteral l => l.Value,
            MetaVariableExpression v when s.IntVars.TryGetValue(v.Name, out var x) => x,
            MetaArrayIndexExpression ai => EvalArrayIndex(s, ai),
            MetaUnaryExpression u => EvalUnaryInt(s, u),
            MetaBinaryExpression b => EvalBinary(s, b),
            MetaSysCallExpression sys => EvalSysInt(s, sys),
            _ => 0
        };
    }

    private string EvalString(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop) return string.Empty;
        // String concatenation via binary Add.
        if (e is MetaBinaryExpression b && b.Op == MetaBinaryOp.Add)
            return EvalString(s, b.Left) + EvalString(s, b.Right);
        return e switch
        {
            MetaStringLiteral l => l.Value,
            MetaVariableExpression v when s.StrVars.TryGetValue(v.Name, out var st) => st,
            MetaSysCallExpression sys => EvalSysString(s, sys),
            _ => EvalInt(s, e).ToString()
        };
    }

    private EntityUid? EvalPtr(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop) return null;
        if (e is MetaVariableExpression v && s.PtrVars.TryGetValue(v.Name, out var ptr)) return ptr;
        if (e is MetaSysCallExpression sys) return EvalSysPtr(s, sys);
        if (e is MetaIntLiteral i && i.Value > 0) return new EntityUid(i.Value);
        return null;
    }

    private List<int> EvalArray(MetaContinuationState s, MetaExpression e)
    {
        if (e is MetaVariableExpression v && s.ArrVars.TryGetValue(v.Name, out var arr))
            return new List<int>(arr);
        if (e is MetaSysCallExpression sys && sys.Name.Equals("GET_CONNECTED", StringComparison.OrdinalIgnoreCase))
        {
            var ptr = EvalPtr(s, sys.Arguments[0]);
            if (ptr == null) return new List<int>();
            return _api.GetConnected(ptr.Value).Select(u => unchecked((int)u.Id)).ToList();
        }
        if (e is MetaSysCallExpression files && files.Name.Equals("GET_FILES", StringComparison.OrdinalIgnoreCase))
        {
            var ptr = EvalPtr(s, files.Arguments[0]);
            if (ptr == null) return new List<int>();
            return _api.GetFiles(ptr.Value).Select(HashToInt).ToList();
        }
        return new List<int>();
    }

    private int EvalArrayIndex(MetaContinuationState s, MetaArrayIndexExpression e)
    {
        if (!s.ArrVars.TryGetValue(e.ArrayName, out var arr))
        {
            s.Error = $"Array '{e.ArrayName}' not found.";
            return 0;
        }
        var idx = EvalInt(s, e.Index);
        if (idx < 0 || idx >= arr.Count)
        {
            s.Error = $"Array index out of bounds for '{e.ArrayName}'.";
            return 0;
        }
        return arr[idx];
    }

    private int EvalUnaryInt(MetaContinuationState s, MetaUnaryExpression u)
    {
        var v = EvalInt(s, u.Operand);
        return u.Op switch
        {
            MetaUnaryOp.Negate => -v,
            MetaUnaryOp.Not => v == 0 ? 1 : 0,
            _ => v
        };
    }

    // ──────────────────────────────────────────────
    //  SYS call evaluation
    // ──────────────────────────────────────────────

    private int EvalSysInt(MetaContinuationState s, MetaSysCallExpression c)
    {
        return c.Name.ToUpperInvariant() switch
        {
            "GET_ICE" => EvalPtr(s, c.Arguments[0]) is { } t ? _api.GetIce(t) : 0,
            "GET_TRACE" => _api.GetTrace(s.DeckUid),
            "ARR_LENGTH" => EvalArray(s, c.Arguments[0]).Count,
            "IS_VALID" => EvalPtr(s, c.Arguments[0]) is { } valid && _api.IsValid(valid) ? 1 : 0,
            _ => 0
        };
    }

    private string EvalSysString(MetaContinuationState s, MetaSysCallExpression c)
    {
        return c.Name.ToUpperInvariant() switch
        {
            "GET_CLASS" => EvalPtr(s, c.Arguments[0]) is { } t ? _api.GetClass(t) : string.Empty,
            _ => string.Empty
        };
    }

    private EntityUid? EvalSysPtr(MetaContinuationState s, MetaSysCallExpression c)
    {
        return c.Name.ToUpperInvariant() switch
        {
            "GET_TARGET" => _api.GetTarget(s.DeckUid),
            "GET_SELF" => _api.GetSelf(s.DeckUid),
            "GET_INTRUDER" => _api.GetIntruder(s.DeckUid),
            "GET_EVENT_SOURCE" => _api.GetEventSource(s.DeckUid),
            "FIND_NEAREST" => _api.FindNearest(s.DeckUid, EvalString(s, c.Arguments[0]), EvalInt(s, c.Arguments[1])),
            _ => null
        };
    }

    private void ExecSysSimple(MetaContinuationState s, MetaSysSimpleInstruction i)
    {
        switch (i.Name.ToUpperInvariant())
        {
            case "CLOAK":
                _api.Cloak(s.DeckUid, EvalInt(s, i.Arguments[0]));
                break;
            case "PING":
                if (EvalPtr(s, i.Arguments[0]) is { } p) _api.Ping(p);
                break;
            case "BURN_NEUROPORT":
                if (EvalPtr(s, i.Arguments[0]) is { } b) _api.BurnNeuroport(b, EvalInt(s, i.Arguments[1]));
                break;
            case "DISCONNECT":
                if (EvalPtr(s, i.Arguments[0]) is { } d) _api.Disconnect(d);
                break;
            case "LOG":
                _api.Log(s.DeckUid, EvalString(s, i.Arguments[0]));
                break;
            case "DOWNLOAD":
                if (EvalPtr(s, i.Arguments[0]) is { } down)
                    _api.Download(s.DeckUid, down, EvalString(s, i.Arguments[1]));
                break;
            case "UPLOAD":
                if (EvalPtr(s, i.Arguments[0]) is { } up)
                    _api.Upload(s.DeckUid, up, EvalString(s, i.Arguments[1]));
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  Binary expression evaluation
    // ──────────────────────────────────────────────

    private int EvalBinary(MetaContinuationState s, MetaBinaryExpression b)
    {
        var l = EvalInt(s, b.Left);
        var r = EvalInt(s, b.Right);
        return b.Op switch
        {
            MetaBinaryOp.Add => l + r,
            MetaBinaryOp.Subtract => l - r,
            MetaBinaryOp.Multiply => l * r,
            MetaBinaryOp.Divide => r == 0 ? 0 : l / r,
            MetaBinaryOp.Modulo => r == 0 ? 0 : l % r,
            MetaBinaryOp.And => (l != 0 && r != 0) ? 1 : 0,
            MetaBinaryOp.Or => (l != 0 || r != 0) ? 1 : 0,
            MetaBinaryOp.Equals => l == r ? 1 : 0,
            MetaBinaryOp.NotEquals => l != r ? 1 : 0,
            MetaBinaryOp.Less => l < r ? 1 : 0,
            MetaBinaryOp.LessOrEqual => l <= r ? 1 : 0,
            MetaBinaryOp.Greater => l > r ? 1 : 0,
            MetaBinaryOp.GreaterOrEqual => l >= r ? 1 : 0,
            _ => 0
        };
    }

    private static void ConsumeGas(MetaContinuationState s, int amount) => s.GasRemaining -= amount;
    private static int HashToInt(string text) => text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// Extended execution result that carries the continuation state for YIELD-resume.
/// </summary>
public sealed record MetaVmRunResult(
    MetaExecutionResult Result,
    MetaContinuationState? Continuation);
