using System;
using System.Collections.Generic;
using Robust.Shared.IoC;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.GameObjects;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaVirtualMachineSystem : EntitySystem
{
    [Dependency] private readonly MetaApiSystem _api = default!;
    [Dependency] private readonly MetaExecutionBudgetSystem _budget = default!;

    public MetaVmRunResult Execute(
        EntityUid deckUid,
        EntityUid userUid,
        EntityUid shardUid,
        MetaBytecode bytecode,
        int gasLimit,
        EntityUid? defenseClearedTarget = null)
    {
        var state = new MetaContinuationState
        {
            DeckUid = GetNetEntity(deckUid),
            UserUid = GetNetEntity(userUid),
            ShardUid = GetNetEntity(shardUid),
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
            ReservedRam = bytecode.RequiredRam,
            DefenseClearedTarget = defenseClearedTarget is { } target ? GetNetEntity(target) : default,
        };

        state.CallStack.Push(new MetaCallFrame(bytecode.Instructions, MetaFrameKind.Block));
        _api.SetUser(deckUid, userUid);
        RunSafely(state);
        _api.SetUser(deckUid, null);
        return FinalizeRun(state);
    }

    public MetaVmRunResult ExecuteEvent(
        EntityUid hostUid,
        EntityUid shardUid,
        MetaBytecode bytecode,
        string eventName,
        EntityUid source,
        int gasLimit)
    {
        var state = new MetaContinuationState
        {
            DeckUid = GetNetEntity(hostUid),
            ShardUid = GetNetEntity(shardUid),
            EventSourceUid = GetNetEntity(source),
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
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
            RunSafely(state);
        }

        _api.SetEventSource(hostUid, null);
        return FinalizeRun(state);
    }

    public MetaVmRunResult Resume(MetaContinuationState state)
    {
        var deckUid = GetEntity(state.DeckUid);
        EntityUid? userUid = state.UserUid == default ? null : GetEntity(state.UserUid);
        EntityUid? eventSource = state.EventSourceUid == default ? null : GetEntity(state.EventSourceUid);
        if (state.SuspensionReason == MetaSuspensionReason.Yield)
            state.GasRemaining = state.InitialGas;

        state.SuspensionReason = MetaSuspensionReason.None;
        _api.SetUser(deckUid, userUid);
        _api.SetEventSource(deckUid, eventSource);
        _api.SetIntruder(deckUid, eventSource);
        RunSafely(state);
        _api.SetUser(deckUid, null);
        _api.SetEventSource(deckUid, null);
        _api.SetIntruder(deckUid, null);
        return FinalizeRun(state);
    }

    public MetaVmRunResult PrepareProtectedExecution(
        EntityUid deckUid,
        EntityUid userUid,
        EntityUid shardUid,
        MetaBytecode bytecode,
        int gasLimit,
        EntityUid target,
        MetaIntrusionWait wait)
    {
        var state = new MetaContinuationState
        {
            DeckUid = GetNetEntity(deckUid),
            UserUid = GetNetEntity(userUid),
            ShardUid = GetNetEntity(shardUid),
            GasRemaining = gasLimit,
            InitialGas = gasLimit,
            ReservedRam = bytecode.RequiredRam,
            AwaitedIntrusionServer = wait.Server,
            AwaitedIntrusionId = wait.Id,
            DefenseClearedTarget = GetNetEntity(target),
            SuspensionReason = MetaSuspensionReason.DefenseResponse,
        };
        state.CallStack.Push(new MetaCallFrame(bytecode.Instructions, MetaFrameKind.Block));
        return FinalizeRun(state);
    }

    private MetaVmRunResult FinalizeRun(MetaContinuationState state)
    {
        if (state.Error != null && state.Failure == MetaExecutionFailure.None)
            state.Failure = MetaExecutionFailure.RuntimeError;

        bool yielded = state.CallStack.Count > 0 && state.Error == null && !state.Exited;
        var deckUid = GetEntity(state.DeckUid);

        var res = new MetaExecutionResult(
            !yielded && state.Error == null, 
            yielded, 
            state.Error,
            state.Failure,
            state.InitialGas - state.GasRemaining,
            state.OperationsThisSlice,
            state.SystemCallsThisSlice,
            state.SuspensionReason,
            state.ReservedRam,
            state.ShardUid);

        if (state.Error != null)
            Logger.ErrorS("meta", $"VM Error on {ToPrettyString(deckUid)}: {state.Error}");

        return new MetaVmRunResult(res, yielded ? state : null);
    }

    private void RunSafely(MetaContinuationState state)
    {
        state.OperationsThisSlice = 0;
        state.SystemCallsThisSlice = 0;
        state.SchedulerPreemptionRequested = false;
        try
        {
            RunLoop(state);
        }
        catch (Exception exception)
        {
            state.Failure = MetaExecutionFailure.RuntimeError;
            state.Error = "RUNTIME ERROR";
            Logger.ErrorS("meta", $"Unhandled META runtime exception: {exception}");
        }
    }

    private void RunLoop(MetaContinuationState s)
    {
        var deckUid = GetEntity(s.DeckUid);
        while (s.CallStack.Count > 0)
        {
            if (s.ShouldStop) return;
            if (s.SchedulerPreemptionRequested)
            {
                s.SuspensionReason = MetaSuspensionReason.SchedulerPreemption;
                return;
            }

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
            // Logger.DebugS("meta", $"[{ToPrettyString(deckUid)}] EXEC: {inst.GetType().Name} (Gas: {s.GasRemaining})");

            ConsumeGas(s, 1);
            if (s.ShouldStop) return;

            if (inst is MetaYieldInstruction y)
            {
                s.ResumeAtTime = y.Milliseconds;
                s.SuspensionReason = MetaSuspensionReason.Yield;
                return;
            }

            ExecuteSingleInstruction(s, inst);
            HandleLoopControl(s);
        }
    }

    private void HandleLoopControl(MetaContinuationState s)
    {
        if (s.BreakRequested)
        {
            while (s.CallStack.Count > 0)
            {
                var popped = s.CallStack.Pop();
                if (popped.Kind is MetaFrameKind.WhileLoop or MetaFrameKind.ForLoop)
                    break;
            }

            s.BreakRequested = false;
            return;
        }

        if (s.ContinueRequested)
        {
            while (s.CallStack.Count > 0)
            {
                var frame = s.CallStack.Peek();
                if (frame.Kind is MetaFrameKind.WhileLoop or MetaFrameKind.ForLoop)
                {
                    frame.InstructionPointer = frame.Code.Count;
                    break;
                }

                s.CallStack.Pop();
            }

            s.ContinueRequested = false;
        }
    }

    private void ExecuteSingleInstruction(MetaContinuationState s, MetaInstruction i)
    {
        var deckUid = GetEntity(s.DeckUid);
        switch (i)
        {
            case MetaDefIntInstruction di:
            {
                s.IntVars[di.Name] = EvalInt(s, di.Value);
                break;
            }
            case MetaDefStrInstruction ds:
            {
                s.StrVars[ds.Name] = EvalString(s, ds.Value);
                break;
            }
            case MetaDefPtrInstruction dp:
            {
                s.PtrVars[dp.Name] = GetNetEntity(EvalPtr(s, dp.Value));
                break;
            }
            case MetaDefArrInstruction darr:
            {
                s.ArrVars[darr.Name] = EvalArray(s, darr.Value);
                break;
            }
            case MetaAssignInstruction asgn:
                ExecuteAssign(s, asgn);
                break;
            case MetaAssignArrayInstruction arrAssign:
                ExecuteArrayAssign(s, arrAssign);
                break;
            case MetaSysLogInstruction l:
                s.SystemCallsThisSlice++;
                _api.MetaLog(deckUid, EvalString(s, l.Message));
                break;
            case MetaSysInjectInstruction inj:
            {
                s.SystemCallsThisSlice++;
                var target = EvalPtr(s, inj.Target);
                var damage = EvalInt(s, inj.Damage);
                if (!s.ShouldStop && target != null)
                {
                    var wait = _api.Inject(deckUid, target.Value, damage, HasDefenseClearance(s, target.Value));
                    if (wait is { } intrusion)
                        SuspendForIntrusion(s, intrusion);
                }
                break;
            }
            case MetaSysOverrideInstruction ov:
            {
                s.SystemCallsThisSlice++;
                var target = EvalPtr(s, ov.Target);
                var key = EvalString(s, ov.Key);
                var value = EvalInt(s, ov.Value);
                if (!s.ShouldStop && target != null)
                    _api.Override(target.Value, key, value);
                break;
            }
            case MetaSysSimpleInstruction ss:
                s.SystemCallsThisSlice++;
                ExecSimple(s, ss);
                break;
            case MetaIfInstruction ifi:
                if (EvalInt(s, ifi.Condition) != 0) s.CallStack.Push(new MetaCallFrame(ifi.ThenBody, MetaFrameKind.Block));
                else if (ifi.ElseBody != null) s.CallStack.Push(new MetaCallFrame(ifi.ElseBody, MetaFrameKind.Block));
                break;
            case MetaWhileInstruction w:
                if (EvalInt(s, w.Condition) != 0) s.CallStack.Push(new MetaCallFrame(w.Body, MetaFrameKind.WhileLoop) { LoopCondition = w.Condition });
                break;
            case MetaForInstruction f:
                if (f.Init != null)
                    ExecuteSingleInstruction(s, f.Init);
                if (!s.ShouldStop && (f.Condition == null || EvalInt(s, f.Condition) != 0))
                    s.CallStack.Push(new MetaCallFrame(f.Body, MetaFrameKind.ForLoop) { LoopCondition = f.Condition, LoopStep = f.Step });
                break;
            case MetaBreakInstruction:
                s.BreakRequested = true;
                break;
            case MetaContinueInstruction:
                s.ContinueRequested = true;
                break;
            case MetaExitInstruction ex:
                s.ExitCode = EvalInt(s, ex.Code);
                s.Exited = true;
                break;
        }
    }

    private void ExecuteAssign(MetaContinuationState s, MetaAssignInstruction asgn)
    {
        if (s.IntVars.ContainsKey(asgn.Name))
        {
            s.IntVars[asgn.Name] = ApplyAssign(asgn.Op, s.IntVars[asgn.Name], EvalInt(s, asgn.Value));
            return;
        }

        if (s.StrVars.ContainsKey(asgn.Name) && asgn.Op == MetaAssignOp.Set)
        {
            s.StrVars[asgn.Name] = EvalString(s, asgn.Value);
            return;
        }

        if (s.PtrVars.ContainsKey(asgn.Name) && asgn.Op == MetaAssignOp.Set)
        {
            s.PtrVars[asgn.Name] = GetNetEntity(EvalPtr(s, asgn.Value));
        }
    }

    private void ExecuteArrayAssign(MetaContinuationState s, MetaAssignArrayInstruction asgn)
    {
        if (!s.ArrVars.TryGetValue(asgn.ArrayName, out var arr))
            return;

        var idx = EvalInt(s, asgn.Index);
        if (idx < 0 || idx >= arr.Count || arr.ElementType != MetaValueType.Int)
            return;

        arr.IntValues[idx] = ApplyAssign(asgn.Op, arr.IntValues[idx], EvalInt(s, asgn.Value));
    }

    private void ExecSimple(MetaContinuationState s, MetaSysSimpleInstruction ss)
    {
        var deckUid = GetEntity(s.DeckUid);
        var func = ss.Name.ToUpperInvariant();
        if (func == "PING")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            if (!s.ShouldStop && target != null)
                _api.Ping(target.Value);
        }
        if (func == "BURN_NEUROPORT")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            var damage = EvalInt(s, ss.Arguments[1]);
            if (!s.ShouldStop && target != null)
                _api.BurnNeuroport(target.Value, damage);
        }
        if (func == "DISCONNECT")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            if (!s.ShouldStop && target != null)
                _api.Disconnect(target.Value);
        }
        if (func == "BREACH")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            if (!s.ShouldStop && target != null)
            {
                var wait = _api.Breach(deckUid, target.Value, HasDefenseClearance(s, target.Value));
                if (wait is { } intrusion)
                    SuspendForIntrusion(s, intrusion);
            }
        }
        if (func == "DUMPSHOCK")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            var damage = EvalInt(s, ss.Arguments[1]);
            if (!s.ShouldStop && target != null)
                _api.ApplyNeuralDamage(target.Value, damage);
        }
        if (func == "CLOAK")
        {
            var strength = EvalInt(s, ss.Arguments[0]);
            if (!s.ShouldStop)
                _api.Cloak(deckUid, strength);
        }
        if (func == "DOWNLOAD")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            var fileId = EvalString(s, ss.Arguments[1]);
            if (!s.ShouldStop && target != null)
                _api.Download(deckUid, target.Value, fileId);
        }
        if (func == "UPLOAD")
        {
            var target = EvalPtr(s, ss.Arguments[0]);
            var fileId = EvalString(s, ss.Arguments[1]);
            if (!s.ShouldStop && target != null)
                _api.Upload(deckUid, target.Value, fileId);
        }
    }

    private int EvalInt(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop)
            return 0;

        if (e is MetaIntLiteral i) return i.Value;
        if (e is MetaVariableExpression v && s.IntVars.TryGetValue(v.Name, out var val)) return val;
        if (e is MetaArrayIndexExpression a && s.ArrVars.TryGetValue(a.ArrayName, out var arr))
        {
            var idx = EvalInt(s, a.Index);
            return idx >= 0 && idx < arr.Count && arr.ElementType == MetaValueType.Int ? arr.IntValues[idx] : 0;
        }
        if (e is MetaUnaryExpression u)
            return u.Op == MetaUnaryOp.Not ? (EvalInt(s, u.Operand) == 0 ? 1 : 0) : -EvalInt(s, u.Operand);
        if (e is MetaBinaryExpression b)
        {
            if (b.Op is MetaBinaryOp.Equals or MetaBinaryOp.NotEquals &&
                (IsStringExpression(s, b.Left) || IsStringExpression(s, b.Right)))
            {
                var eq = string.Equals(EvalString(s, b.Left), EvalString(s, b.Right), StringComparison.Ordinal);
                return b.Op == MetaBinaryOp.Equals ? (eq ? 1 : 0) : (eq ? 0 : 1);
            }

            return ApplyBin(b.Op, EvalInt(s, b.Left), EvalInt(s, b.Right));
        }
        if (e is MetaSysCallExpression sys) return EvalSysInt(s, sys);
        return 0;
    }

    private int EvalSysInt(MetaContinuationState s, MetaSysCallExpression sys)
    {
        s.SystemCallsThisSlice++;
        var f = sys.Name.ToUpperInvariant();
        if (f == "GET_ICE")
        {
            var target = EvalPtr(s, sys.Arguments[0]);
            return !s.ShouldStop && target != null ? _api.GetIce(target.Value) : 0;
        }
        if (f == "GET_TRACE") return _api.GetTrace(GetEntity(s.DeckUid));
        if (f == "IS_VALID")
        {
            var target = EvalPtr(s, sys.Arguments[0]);
            return !s.ShouldStop && target != null && _api.IsValid(target.Value) ? 1 : 0;
        }
        if (f == "ARR_LENGTH") { if (sys.Arguments[0] is MetaVariableExpression av && s.ArrVars.TryGetValue(av.Name, out var arr)) return arr.Count; }
        if (f == "GET_GAS") return s.GasRemaining;
        if (f == "GET_RAM_AVAILABLE" && TryComp<CyberdeckComponent>(GetEntity(s.DeckUid), out var deck)) return deck.CurrentRam;
        if (f == "HAS_ROOT")
        {
            var target = EvalPtr(s, sys.Arguments[0]);
            return !s.ShouldStop && target != null &&
                   _api.HasRoot(GetEntity(s.DeckUid), target.Value) ? 1 : 0;
        }
        if (f == "ROOT")
        {
            var target = EvalPtr(s, sys.Arguments[0]);
            var strength = EvalInt(s, sys.Arguments[1]);
            return !s.ShouldStop && target != null &&
                   _api.TryRoot(GetEntity(s.DeckUid), target.Value, strength) ? 1 : 0;
        }
        return 0;
    }

    private EntityUid? EvalPtr(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop)
            return null;

        if (e is MetaVariableExpression v && s.PtrVars.TryGetValue(v.Name, out var p)) return GetEntity(p);
        if (e is MetaArrayIndexExpression a && s.ArrVars.TryGetValue(a.ArrayName, out var arr))
        {
            var idx = EvalInt(s, a.Index);
            return idx >= 0 && idx < arr.Count && arr.ElementType == MetaValueType.Ptr ? GetEntity(arr.PtrValues[idx]) : null;
        }
        if (e is MetaSysCallExpression sys) return EvalSysPtr(s, sys);
        return null;
    }

    private EntityUid? EvalSysPtr(MetaContinuationState s, MetaSysCallExpression sys)
    {
        s.SystemCallsThisSlice++;
        var deckUid = GetEntity(s.DeckUid);
        var f = sys.Name.ToUpperInvariant();
        if (f == "GET_TARGET") return _api.GetTarget(deckUid);
        if (f == "GET_SERVER") return _api.GetServer(deckUid);
        if (f == "GET_SELF") return _api.GetSelf(deckUid);
        if (f == "GET_INTRUDER") return _api.GetIntruder(deckUid);
        if (f == "GET_EVENT_SOURCE") return _api.GetEventSource(deckUid);
        if (f == "FIND_NEAREST")
        {
            var className = EvalString(s, sys.Arguments[0]);
            var radius = EvalInt(s, sys.Arguments[1]);
            return !s.ShouldStop ? _api.FindNearest(deckUid, className, radius) : null;
        }
        if (f is "SPAWN_ICE" or "SPAWN_BLACK_ICE" or "SPAWN_DEMON")
        {
            var target = EvalPtr(s, sys.Arguments[0]);
            var strength = EvalInt(s, sys.Arguments[1]);
            if (s.ShouldStop || target == null)
                return null;

            return f switch
            {
                "SPAWN_ICE" => _api.SpawnIce(deckUid, target.Value, strength, false),
                "SPAWN_BLACK_ICE" => _api.SpawnIce(deckUid, target.Value, strength, true),
                _ => _api.SpawnDemon(deckUid, target.Value, strength)
            };
        }
        return null;
    }

    private MetaArrayValue EvalArray(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop)
            return new MetaArrayValue();

        if (e is MetaSysCallExpression sys && sys.Name.ToUpperInvariant() == "GET_CONNECTED")
        {
            s.SystemCallsThisSlice++;
            var t = EvalPtr(s, sys.Arguments[0]);
            if (s.ShouldStop || t == null) return new MetaArrayValue { ElementType = MetaValueType.Ptr };
            var ents = _api.GetConnected(t.Value);
            return new MetaArrayValue
            {
                ElementType = MetaValueType.Ptr,
                PtrValues = ents.Select(uid => GetNetEntity(uid)).ToList()
            };
        }

        if (e is MetaSysCallExpression fileSys && fileSys.Name.ToUpperInvariant() == "GET_FILES")
        {
            s.SystemCallsThisSlice++;
            var t = EvalPtr(s, fileSys.Arguments[0]);
            return new MetaArrayValue
            {
                ElementType = MetaValueType.Str,
                StrValues = !s.ShouldStop && t != null ? _api.GetFiles(t.Value).ToList() : new List<string>()
            };
        }

        if (e is MetaSysCallExpression vitalsSys && vitalsSys.Name.ToUpperInvariant() == "GET_VITALS")
        {
            s.SystemCallsThisSlice++;
            var t = EvalPtr(s, vitalsSys.Arguments[0]);
            return new MetaArrayValue
            {
                ElementType = MetaValueType.Int,
                IntValues = !s.ShouldStop && t != null ? _api.GetVitals(t.Value).ToList() : new List<int>()
            };
        }

        return new MetaArrayValue();
    }

    private string EvalString(MetaContinuationState s, MetaExpression e)
    {
        ConsumeGas(s, 1);
        if (s.ShouldStop)
            return "";

        if (e is MetaStringLiteral sl) return sl.Value ?? "";
        if (e is MetaIntLiteral il) return il.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (e is MetaVariableExpression v)
        {
            if (s.StrVars.TryGetValue(v.Name, out var sv)) return sv ?? "";
            if (s.IntVars.TryGetValue(v.Name, out var iv)) return iv.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.PtrVars.TryGetValue(v.Name, out var pv)) return pv.ToString() ?? "";
        }
        if (e is MetaArrayIndexExpression a && s.ArrVars.TryGetValue(a.ArrayName, out var arr))
        {
            var idx = EvalInt(s, a.Index);
            return idx >= 0 && idx < arr.Count && arr.ElementType == MetaValueType.Str ? arr.StrValues[idx] : "";
        }
        if (e is MetaSysCallExpression sys)
        {
            s.SystemCallsThisSlice++;
            var f = sys.Name.ToUpperInvariant();
            if (f == "GET_CLASS") { var t = EvalPtr(s, sys.Arguments[0]); return t != null ? (_api.GetClass(t.Value) ?? "") : ""; }
        }
        if (e is MetaBinaryExpression b && b.Op == MetaBinaryOp.Add && (IsStringExpression(s, b.Left) || IsStringExpression(s, b.Right)))
            return EvalString(s, b.Left) + EvalString(s, b.Right);
        return "";
    }

    private int ApplyBin(MetaBinaryOp op, int l, int r) => op switch {
        MetaBinaryOp.Add => l + r,
        MetaBinaryOp.Subtract => l - r,
        MetaBinaryOp.Multiply => l * r,
        MetaBinaryOp.Divide => r == 0 ? 0 : l / r,
        MetaBinaryOp.Modulo => r == 0 ? 0 : l % r,
        MetaBinaryOp.And => l != 0 && r != 0 ? 1 : 0,
        MetaBinaryOp.Or => l != 0 || r != 0 ? 1 : 0,
        MetaBinaryOp.Equals => l == r ? 1 : 0,
        MetaBinaryOp.NotEquals => l != r ? 1 : 0,
        MetaBinaryOp.Greater => l > r ? 1 : 0,
        MetaBinaryOp.GreaterOrEqual => l >= r ? 1 : 0,
        MetaBinaryOp.Less => l < r ? 1 : 0,
        MetaBinaryOp.LessOrEqual => l <= r ? 1 : 0,
        _ => 0
    };

    private int ApplyAssign(MetaAssignOp op, int old, int val) => op switch {
        MetaAssignOp.Set => val, MetaAssignOp.AddAssign => old + val, MetaAssignOp.SubAssign => old - val, _ => val
    };

    private bool IsStringExpression(MetaContinuationState s, MetaExpression e)
    {
        return e switch
        {
            MetaStringLiteral => true,
            MetaVariableExpression v => s.StrVars.ContainsKey(v.Name),
            MetaArrayIndexExpression a => s.ArrVars.TryGetValue(a.ArrayName, out var arr) && arr.ElementType == MetaValueType.Str,
            MetaSysCallExpression sys => sys.Name.Equals("GET_CLASS", StringComparison.OrdinalIgnoreCase),
            MetaBinaryExpression b when b.Op == MetaBinaryOp.Add => IsStringExpression(s, b.Left) || IsStringExpression(s, b.Right),
            _ => false
        };
    }

    private void ConsumeGas(MetaContinuationState s, int gas)
    {
        if (!_budget.TryConsume())
            s.SchedulerPreemptionRequested = true;

        if (gas <= s.GasRemaining)
        {
            s.GasRemaining -= gas;
            s.OperationsThisSlice += gas;
            if (s.OperationsThisSlice >= _budget.ProcessQuantum)
                s.SchedulerPreemptionRequested = true;
            return;
        }

        s.GasRemaining = 0;
        s.Failure = MetaExecutionFailure.GasExhausted;
        s.Error = "GAS LIMIT EXCEEDED";
    }

    private static void SuspendForIntrusion(MetaContinuationState state, MetaIntrusionWait wait)
    {
        state.AwaitedIntrusionServer = wait.Server;
        state.AwaitedIntrusionId = wait.Id;
        state.SuspensionReason = MetaSuspensionReason.DefenseResponse;
    }

    private bool HasDefenseClearance(MetaContinuationState state, EntityUid target)
    {
        return state.DefenseClearedTarget != default &&
               GetEntity(state.DefenseClearedTarget) == target;
    }
}

public sealed record MetaVmRunResult(MetaExecutionResult Result, MetaContinuationState? Continuation);
