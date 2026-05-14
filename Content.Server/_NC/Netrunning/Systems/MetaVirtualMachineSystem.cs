using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.GameObjects;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaVirtualMachineSystem : EntitySystem
{
    [Dependency] private readonly MetaApiSystem _api = default!;

    // --- RAM Cost Constants ---
    private const int VariableRamCost = 1;
    private const int SysBaseCost = 1;
    private const int SysHeavyCost = 2;
    private const int SysCriticalCost = 5;
    private const int SysNetworkCost = 8;

    public MetaVmRunResult Execute(EntityUid deckUid, EntityUid userUid, EntityUid shardUid, MetaBytecode bytecode, int gasLimit)
    {
        var state = new MetaContinuationState
        {
            DeckUid = GetNetEntity(deckUid),
            UserUid = GetNetEntity(userUid),
            ShardUid = GetNetEntity(shardUid),
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
        };

        state.CallStack.Push(new MetaCallFrame(bytecode.Instructions, MetaFrameKind.Block));
        _api.SetUser(deckUid, userUid);
        RunLoop(state);
        _api.SetUser(deckUid, null);
        return FinalizeRun(state);
    }

    public MetaVmRunResult ExecuteEvent(EntityUid hostUid, MetaBytecode bytecode, string eventName, EntityUid source)
    {
        var state = new MetaContinuationState
        {
            DeckUid = GetNetEntity(hostUid),
            ShardUid = NetEntity.Invalid,
            GasRemaining = 1000,
            InitialGas = 1000,
        };

        _api.SetEventSource(hostUid, source);

        foreach (var inst in bytecode.Instructions)
        {
            if (inst is MetaOnEventInstruction onEvent && onEvent.EventName == eventName)
            {
                state.CallStack.Push(new MetaCallFrame(onEvent.Body, MetaFrameKind.Block));
                break;
            }
        }

        if (state.CallStack.Count > 0)
        {
            _api.SetUser(hostUid, null); // Defensive daemons don't have a physical player user
            RunLoop(state);
        }

        _api.SetEventSource(hostUid, null);
        return FinalizeRun(state);
    }

    public MetaVmRunResult Resume(MetaContinuationState state)
    {
        var deckUid = GetEntity(state.DeckUid);
        var userUid = GetEntity(state.UserUid);
        _api.SetUser(deckUid, userUid);
        RunLoop(state);
        _api.SetUser(deckUid, null);
        return FinalizeRun(state);
    }

    private MetaVmRunResult FinalizeRun(MetaContinuationState state)
    {
        bool yielded = state.CallStack.Count > 0 && state.Error == null && !state.Exited;
        int leak = 0;

        var deckUid = GetEntity(state.DeckUid);

        if (!yielded)
        {
            leak = Math.Max(0, state.AllocatedRam - state.FreedRam);
            if (leak > 0 && TryComp<CyberdeckComponent>(deckUid, out var deck))
            {
                deck.LeakedRam = Math.Min(deck.MaxRam, deck.LeakedRam + leak);
                Dirty(deckUid, deck);
            }
            state.VariablesUsed = 0; // Reset for potential next run of the same state object
        }

        var res = new MetaExecutionResult(
            !yielded && state.Error == null, 
            yielded, 
            state.Error, 
            state.InitialGas - state.GasRemaining, 
            leak,
            state.ShardUid);

        if (state.Error != null)
            Logger.ErrorS("meta", $"VM Error on {ToPrettyString(deckUid)}: {state.Error}");

        return new MetaVmRunResult(res, yielded ? state : null);
    }

    private void RunLoop(MetaContinuationState s)
    {
        var deckUid = GetEntity(s.DeckUid);
        while (s.CallStack.Count > 0)
        {
            if (s.ShouldStop) return;
            var frame = s.CallStack.Peek();

            if (frame.InstructionPointer >= frame.Code.Count)
            {
                if (frame.Kind == MetaFrameKind.WhileLoop && !s.BreakRequested)
                {
                    if (frame.LoopCondition != null && EvalInt(s, frame.LoopCondition) != 0)
                    {
                        frame.InstructionPointer = 0;
                        continue;
                    }
                }
                else if (frame.Kind == MetaFrameKind.ForLoop && !s.BreakRequested)
                {
                    if (frame.LoopStep != null) ExecuteSingleInstruction(s, frame.LoopStep);
                    if (!s.ShouldStop && (frame.LoopCondition == null || EvalInt(s, frame.LoopCondition) != 0))
                    {
                        frame.InstructionPointer = 0;
                        continue;
                    }
                }
                s.CallStack.Pop();
                s.BreakRequested = false;
                continue;
            }

            var inst = frame.Code[frame.InstructionPointer++];
            
            // Execution Debug Logging
            // Logger.DebugS("meta", $"[{ToPrettyString(deckUid)}] EXEC: {inst.GetType().Name} (Gas: {s.GasRemaining}, RAM: {s.AllocatedRam - s.VariablesUsed})");

            ConsumeGas(s, 1);
            if (s.ShouldStop) return;

            if (inst is MetaYieldInstruction y)
            {
                s.ResumeAtTime = y.Milliseconds;
                return;
            }

            ExecuteSingleInstruction(s, inst);
        }
    }

    private bool CheckRam(MetaContinuationState s, int cost)
    {
        if (s.AllocatedRam - s.VariablesUsed >= cost) return true;
        s.Error = "[FATAL] META: RAM OVERFLOW. Insufficient allocated memory.";
        return false;
    }

    private void ExecuteSingleInstruction(MetaContinuationState s, MetaInstruction i)
    {
        var deckUid = GetEntity(s.DeckUid);
        switch (i)
        {
            case MetaAllocRamInstruction a:
            {
                var amount = Math.Max(0, a.Amount);
                if (TryComp<CyberdeckComponent>(deckUid, out var d) && d.CurrentRam >= amount)
                {
                    d.CurrentRam -= amount;
                    s.AllocatedRam += amount;
                    Dirty(deckUid, d);
                }
                else s.Error = "OUT OF MEMORY";
                break;
            }
            case MetaFreeRamInstruction f:
            {
                var toFree = Math.Clamp(f.Amount, 0, s.AllocatedRam - s.FreedRam);
                if (TryComp<CyberdeckComponent>(deckUid, out var fd))
                {
                    fd.CurrentRam = Math.Min(fd.MaxRam, fd.CurrentRam + toFree);
                    s.FreedRam += toFree;
                    Dirty(deckUid, fd);
                }
                break;
            }
            case MetaDefIntInstruction di:
            {
                if (CheckRam(s, VariableRamCost)) { s.IntVars[di.Name] = EvalInt(s, di.Value); s.VariablesUsed += VariableRamCost; }
                break;
            }
            case MetaDefStrInstruction ds:
            {
                if (CheckRam(s, VariableRamCost)) { s.StrVars[ds.Name] = EvalString(s, ds.Value); s.VariablesUsed += VariableRamCost; }
                break;
            }
            case MetaDefPtrInstruction dp:
            {
                if (CheckRam(s, VariableRamCost)) { s.PtrVars[dp.Name] = GetNetEntity(EvalPtr(s, dp.Value)); s.VariablesUsed += VariableRamCost; }
                break;
            }
            case MetaDefArrInstruction darr:
            {
                if (CheckRam(s, VariableRamCost)) { s.ArrVars[darr.Name] = EvalArray(s, darr.Value); s.VariablesUsed += VariableRamCost; }
                break;
            }
            case MetaAssignInstruction asgn:
                if (s.IntVars.ContainsKey(asgn.Name)) s.IntVars[asgn.Name] = ApplyAssign(asgn.Op, s.IntVars[asgn.Name], EvalInt(s, asgn.Value));
                break;
            case MetaSysLogInstruction l:
                _api.MetaLog(deckUid, EvalString(s, l.Message));
                break;
            case MetaSysInjectInstruction inj:
                if (CheckRam(s, SysCriticalCost)) { var t = EvalPtr(s, inj.Target); if (t != null) _api.Inject(deckUid, t.Value, EvalInt(s, inj.Damage)); }
                break;
            case MetaSysOverrideInstruction ov:
                if (CheckRam(s, SysHeavyCost)) { var t = EvalPtr(s, ov.Target); if (t != null) _api.Override(t.Value, EvalString(s, ov.Key), EvalInt(s, ov.Value)); }
                break;
            case MetaSysSimpleInstruction ss:
                ExecSimple(s, ss);
                break;
            case MetaIfInstruction ifi:
                if (EvalInt(s, ifi.Condition) != 0) s.CallStack.Push(new MetaCallFrame(ifi.ThenBody, MetaFrameKind.Block));
                else if (ifi.ElseBody != null) s.CallStack.Push(new MetaCallFrame(ifi.ElseBody, MetaFrameKind.Block));
                break;
            case MetaWhileInstruction w:
                if (EvalInt(s, w.Condition) != 0) s.CallStack.Push(new MetaCallFrame(w.Body, MetaFrameKind.WhileLoop) { LoopCondition = w.Condition });
                break;
            case MetaExitInstruction ex:
                s.Exited = true;
                break;
        }
    }

    private void ExecSimple(MetaContinuationState s, MetaSysSimpleInstruction ss)
    {
        var deckUid = GetEntity(s.DeckUid);
        var func = ss.Name.ToUpperInvariant();
        if (func == "PING" && CheckRam(s, SysBaseCost)) { var t = EvalPtr(s, ss.Arguments[0]); if (t != null) _api.Ping(t.Value); }
        if (func == "BURN_NEUROPORT" && CheckRam(s, SysCriticalCost)) { var t = EvalPtr(s, ss.Arguments[0]); if (t != null) _api.BurnNeuroport(t.Value, EvalInt(s, ss.Arguments[1])); }
        if (func == "DISCONNECT" && CheckRam(s, SysHeavyCost)) { var t = EvalPtr(s, ss.Arguments[0]); if (t != null) _api.Disconnect(t.Value); }
    }

    private int EvalInt(MetaContinuationState s, MetaExpression e)
    {
        if (e is MetaIntLiteral i) return i.Value;
        if (e is MetaVariableExpression v && s.IntVars.TryGetValue(v.Name, out var val)) return val;
        if (e is MetaBinaryExpression b) return ApplyBin(b.Op, EvalInt(s, b.Left), EvalInt(s, b.Right));
        if (e is MetaSysCallExpression sys) return EvalSysInt(s, sys);
        return 0;
    }

    private int EvalSysInt(MetaContinuationState s, MetaSysCallExpression sys)
    {
        var f = sys.Name.ToUpperInvariant();
        if (f == "GET_ICE") { var t = EvalPtr(s, sys.Arguments[0]); return t != null ? _api.GetIce(t.Value) : 0; }
        if (f == "GET_TRACE") return _api.GetTrace(GetEntity(s.DeckUid));
        if (f == "IS_VALID" && CheckRam(s, SysBaseCost)) { var t = EvalPtr(s, sys.Arguments[0]); return t != null && _api.IsValid(t.Value) ? 1 : 0; }
        if (f == "ARR_LENGTH") { if (sys.Arguments[0] is MetaVariableExpression av && s.ArrVars.TryGetValue(av.Name, out var arr)) return arr.Count; }
        if (f == "GET_GAS") return s.GasRemaining;
        if (f == "GET_RAM_AVAILABLE") return s.AllocatedRam - s.VariablesUsed;
        return 0;
    }

    private EntityUid? EvalPtr(MetaContinuationState s, MetaExpression e)
    {
        if (e is MetaVariableExpression v && s.PtrVars.TryGetValue(v.Name, out var p)) return GetEntity(p);
        if (e is MetaSysCallExpression sys) return EvalSysPtr(s, sys);
        return null;
    }

    private EntityUid? EvalSysPtr(MetaContinuationState s, MetaSysCallExpression sys)
    {
        var deckUid = GetEntity(s.DeckUid);
        var f = sys.Name.ToUpperInvariant();
        if (f == "GET_TARGET" && CheckRam(s, SysBaseCost)) return _api.GetTarget(deckUid);
        if (f == "GET_SELF") return _api.GetSelf(deckUid);
        if (f == "GET_INTRUDER") return _api.GetIntruder(deckUid);
        if (f == "GET_EVENT_SOURCE") return _api.GetEventSource(deckUid);
        if (f == "FIND_NEAREST" && CheckRam(s, SysNetworkCost)) return _api.FindNearest(deckUid, EvalString(s, sys.Arguments[0]), EvalInt(s, sys.Arguments[1]));
        return null;
    }

    private List<int> EvalArray(MetaContinuationState s, MetaExpression e)
    {
        if (e is MetaSysCallExpression sys && sys.Name.ToUpperInvariant() == "GET_CONNECTED")
        {
            if (!CheckRam(s, SysNetworkCost)) return new List<int>();
            var t = EvalPtr(s, sys.Arguments[0]);
            if (t == null) return new List<int>();
            var ents = _api.GetConnected(t.Value);
            return ents.Select(uid => (int)uid).ToList();
        }
        return new List<int>();
    }

    private string EvalString(MetaContinuationState s, MetaExpression e)
    {
        if (e is MetaStringLiteral sl) return sl.Value ?? "";
        if (e is MetaIntLiteral il) return il.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (e is MetaVariableExpression v)
        {
            if (s.StrVars.TryGetValue(v.Name, out var sv)) return sv ?? "";
            if (s.IntVars.TryGetValue(v.Name, out var iv)) return iv.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.PtrVars.TryGetValue(v.Name, out var pv)) return pv.ToString() ?? "";
        }
        if (e is MetaSysCallExpression sys)
        {
            var f = sys.Name.ToUpperInvariant();
            if (f == "GET_CLASS" && CheckRam(s, SysHeavyCost)) { var t = EvalPtr(s, sys.Arguments[0]); return t != null ? (_api.GetClass(t.Value) ?? "") : ""; }
            if (f == "INTERCEPT_PDA") { var t = EvalPtr(s, sys.Arguments[0]); return t != null ? (_api.InterceptPda(t.Value) ?? "") : ""; }
        }
        if (e is MetaBinaryExpression b && b.Op == MetaBinaryOp.Add) return EvalString(s, b.Left) + EvalString(s, b.Right);
        return "";
    }

    private int ApplyBin(MetaBinaryOp op, int l, int r) => op switch {
        MetaBinaryOp.Add => l + r, MetaBinaryOp.Subtract => l - r, MetaBinaryOp.Multiply => l * r,
        MetaBinaryOp.Divide => r == 0 ? 0 : l / r, MetaBinaryOp.Equals => l == r ? 1 : 0,
        MetaBinaryOp.NotEquals => l != r ? 1 : 0, MetaBinaryOp.Greater => l > r ? 1 : 0,
        MetaBinaryOp.Less => l < r ? 1 : 0, _ => 0
    };

    private int ApplyAssign(MetaAssignOp op, int old, int val) => op switch {
        MetaAssignOp.Set => val, MetaAssignOp.AddAssign => old + val, MetaAssignOp.SubAssign => old - val, _ => val
    };

    private void ConsumeGas(MetaContinuationState s, int gas) { s.GasRemaining -= gas; if (s.GasRemaining <= 0) s.Error = "GAS LIMIT EXCEEDED"; }
}

public sealed record MetaVmRunResult(MetaExecutionResult Result, MetaContinuationState? Continuation);
