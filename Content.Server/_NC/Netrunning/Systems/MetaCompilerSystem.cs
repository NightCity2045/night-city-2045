using System;
using System.Collections.Generic;
using Robust.Shared.IoC;
using Robust.Shared.GameObjects;
using Content.Server._NC.Netrunning.Meta;
using Content.Shared._NC.Netrunning.Meta;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaCompilerSystem : EntitySystem
{
    private static readonly Dictionary<string, (int Min, int Max)> SysArity = new()
    {
        ["GET_TARGET"] = (0, 0),
        ["GET_SELF"] = (0, 0),
        ["GET_CONNECTED"] = (1, 1),
        ["GET_CLASS"] = (1, 1),
        ["GET_ICE"] = (1, 1),
        ["INJECT"] = (2, 2),
        ["GET_TRACE"] = (0, 0),
        ["CLOAK"] = (1, 1),
        ["OVERRIDE"] = (3, 3),
        ["PING"] = (1, 1),
        ["GET_INTRUDER"] = (0, 0),
        ["BURN_NEUROPORT"] = (2, 2),
        ["DISCONNECT"] = (1, 1),
        ["IS_VALID"] = (1, 1),
        ["GET_EVENT_SOURCE"] = (0, 0),
        ["FIND_NEAREST"] = (2, 2),
        ["GET_FILES"] = (1, 1),
        ["DOWNLOAD"] = (2, 2),
        ["UPLOAD"] = (2, 2),
        ["ARR_LENGTH"] = (1, 1),
        ["LOG"] = (1, 1),
    };

    public bool TryCompile(string source, MetaProgramKind kind, out MetaBytecode? bytecode, out string? error)
    {
        bytecode = null;
        error = null;

        var lexer = new MetaLexer(source);
        var tokens = lexer.Tokenize(out error);
        if (error != null)
            return false;

        var parser = new MetaParser(tokens);
        var instructions = parser.ParseProgram(out error);
        if (instructions == null || error != null)
            return false;

        var ctx = new ValidationContext();
        if (!ValidateInstructions(instructions, ctx, kind, out error))
            return false;

        var requiredRam = EstimateRam(instructions);
        bytecode = new MetaBytecode(instructions, requiredRam, kind);
        return true;
    }

    private bool ValidateInstructions(List<MetaInstruction> code, ValidationContext ctx, MetaProgramKind kind, out string? error)
    {
        foreach (var instruction in code)
        {
            if (!ValidateInstruction(instruction, ctx, kind, out error))
                return false;
        }

        error = null;
        return true;
    }

    private bool ValidateInstruction(MetaInstruction instruction, ValidationContext ctx, MetaProgramKind kind, out string? error)
    {
        switch (instruction)
        {
            case MetaDefIntInstruction i:
                if (!ExpectType(i.Value, MetaValueType.Int, ctx, out error)) return false;
                ctx.Types[i.Name] = MetaValueType.Int;
                break;
            case MetaDefStrInstruction s:
                if (!ExpectType(s.Value, MetaValueType.Str, ctx, out error)) return false;
                ctx.Types[s.Name] = MetaValueType.Str;
                break;
            case MetaDefPtrInstruction p:
                if (!ExpectType(p.Value, MetaValueType.Ptr, ctx, out error)) return false;
                ctx.Types[p.Name] = MetaValueType.Ptr;
                break;
            case MetaDefArrInstruction a:
                if (!ExpectType(a.Value, MetaValueType.Arr, ctx, out error)) return false;
                ctx.Types[a.Name] = MetaValueType.Arr;
                break;
            case MetaAssignInstruction assign:
                if (!ctx.Types.TryGetValue(assign.Name, out var targetType))
                {
                    error = $"Compilation Error: Unknown variable '{assign.Name}'.";
                    return false;
                }
                if (!ExpectType(assign.Value, targetType, ctx, out error)) return false;
                break;
            case MetaAssignArrayInstruction arrAssign:
                if (!ctx.Types.TryGetValue(arrAssign.ArrayName, out var arrType) || arrType != MetaValueType.Arr)
                {
                    error = $"Compilation Error: '{arrAssign.ArrayName}' is not ARR.";
                    return false;
                }
                if (!ExpectType(arrAssign.Index, MetaValueType.Int, ctx, out error)) return false;
                break;
            case MetaIfInstruction ifi:
                if (!ExpectType(ifi.Condition, MetaValueType.Int, ctx, out error)) return false;
                if (!ValidateInstructions(ifi.ThenBody, ctx.PushLoop(ctx.LoopDepth), kind, out error)) return false;
                if (ifi.ElseBody != null && !ValidateInstructions(ifi.ElseBody, ctx.PushLoop(ctx.LoopDepth), kind, out error)) return false;
                break;
            case MetaWhileInstruction w:
                if (!ExpectType(w.Condition, MetaValueType.Int, ctx, out error)) return false;
                if (!ContainsYield(w.Body))
                {
                    error = "ERROR: Цикл без паузы. Угроза зависания оборудования.";
                    return false;
                }
                if (!ValidateInstructions(w.Body, ctx.PushLoop(ctx.LoopDepth + 1), kind, out error)) return false;
                break;
            case MetaForInstruction f:
                var loopCtx = ctx.PushLoop(ctx.LoopDepth + 1);
                if (f.Init != null && !ValidateInstruction(f.Init, loopCtx, kind, out error)) return false;
                if (f.Condition != null && !ExpectType(f.Condition, MetaValueType.Int, loopCtx, out error)) return false;
                if (f.Step != null && !ValidateInstruction(f.Step, loopCtx, kind, out error)) return false;
                if (!ContainsYield(f.Body))
                {
                    error = "ERROR: Цикл без паузы. Угроза зависания оборудования.";
                    return false;
                }
                if (!ValidateInstructions(f.Body, loopCtx, kind, out error)) return false;
                break;
            case MetaOnEventInstruction evt:
                if (kind != MetaProgramKind.DaemonDefensive)
                {
                    error = "Compilation Error: ON_EVENT is only allowed in DAEMON_DEFENSIVE programs.";
                    return false;
                }
                if (!evt.EventName.Equals("INTRUSION", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Compilation Error: Unsupported event '{evt.EventName}'.";
                    return false;
                }
                if (!ValidateInstructions(evt.Body, ctx.PushLoop(ctx.LoopDepth), kind, out error))
                    return false;
                break;
            case MetaBreakInstruction:
            case MetaContinueInstruction:
                if (ctx.LoopDepth <= 0)
                {
                    error = "Compilation Error: BREAK/CONTINUE outside loop.";
                    return false;
                }
                break;
            case MetaYieldInstruction y:
                if (y.Milliseconds < 0)
                {
                    error = "Compilation Error: YIELD must be non-negative.";
                    return false;
                }
                break;
            case MetaExitInstruction ex:
                if (!ExpectType(ex.Code, MetaValueType.Int, ctx, out error)) return false;
                break;
            case MetaSysLogInstruction slog:
                if (!ExpectType(slog.Message, MetaValueType.Str, ctx, out error)) return false;
                break;
            case MetaSysInjectInstruction inj:
                if (!ExpectType(inj.Target, MetaValueType.Ptr, ctx, out error)) return false;
                if (!ExpectType(inj.Damage, MetaValueType.Int, ctx, out error)) return false;
                break;
            case MetaSysOverrideInstruction ovr:
                if (!ExpectType(ovr.Target, MetaValueType.Ptr, ctx, out error)) return false;
                if (!ExpectType(ovr.Key, MetaValueType.Str, ctx, out error)) return false;
                if (!ExpectType(ovr.Value, MetaValueType.Int, ctx, out error)) return false;
                break;
            case MetaSysSimpleInstruction simple:
                if (!ValidateSysArity(simple.Name, simple.Arguments.Count, out error)) return false;
                if (!ValidateSysArgTypes(simple.Name, simple.Arguments, ctx, out error)) return false;
                break;
            case MetaAllocRamInstruction:
            case MetaFreeRamInstruction:
                break;
            default:
                error = $"Compilation Error: Unsupported instruction {instruction.GetType().Name}.";
                return false;
        }

        error = null;
        return true;
    }

    private bool ExpectType(MetaExpression expr, MetaValueType expected, ValidationContext ctx, out string? error)
    {
        if (!InferType(expr, ctx, out var actual, out error))
            return false;

        if (actual != expected)
        {
            error = $"Compilation Error: Type mismatch. Expected {expected}, got {actual}.";
            return false;
        }

        return true;
    }

    private bool InferType(MetaExpression expr, ValidationContext ctx, out MetaValueType type, out string? error)
    {
        switch (expr)
        {
            case MetaIntLiteral:
                type = MetaValueType.Int; error = null; return true;
            case MetaStringLiteral:
                type = MetaValueType.Str; error = null; return true;
            case MetaVariableExpression v:
                if (!ctx.Types.TryGetValue(v.Name, out type))
                {
                    error = $"Compilation Error: Unknown variable '{v.Name}'.";
                    return false;
                }
                error = null;
                return true;
            case MetaArrayIndexExpression a:
                if (!ctx.Types.TryGetValue(a.ArrayName, out var arrType) || arrType != MetaValueType.Arr)
                {
                    error = $"Compilation Error: '{a.ArrayName}' is not ARR.";
                    type = MetaValueType.Int;
                    return false;
                }
                if (!ExpectType(a.Index, MetaValueType.Int, ctx, out error))
                {
                    type = MetaValueType.Int;
                    return false;
                }
                type = MetaValueType.Int;
                return true;
            case MetaUnaryExpression u:
                if (u.Op == MetaUnaryOp.Not)
                {
                    type = MetaValueType.Int;
                    return ExpectType(u.Operand, MetaValueType.Int, ctx, out error);
                }
                type = MetaValueType.Int;
                return ExpectType(u.Operand, MetaValueType.Int, ctx, out error);
            case MetaBinaryExpression b:
                if (!InferType(b.Left, ctx, out var lt, out error)) { type = MetaValueType.Int; return false; }
                if (!InferType(b.Right, ctx, out var rt, out error)) { type = MetaValueType.Int; return false; }
                type = b.Op is MetaBinaryOp.And or MetaBinaryOp.Or or MetaBinaryOp.Equals or MetaBinaryOp.NotEquals or MetaBinaryOp.Less or MetaBinaryOp.LessOrEqual or MetaBinaryOp.Greater or MetaBinaryOp.GreaterOrEqual ? MetaValueType.Int : lt;
                if (b.Op == MetaBinaryOp.Add && lt == MetaValueType.Str && rt == MetaValueType.Str)
                {
                    type = MetaValueType.Str;
                    error = null;
                    return true;
                }
                if (lt != rt && b.Op is not MetaBinaryOp.Equals and not MetaBinaryOp.NotEquals)
                {
                    error = $"Compilation Error: Binary op type mismatch {lt} vs {rt}.";
                    return false;
                }
                error = null;
                return true;
            case MetaSysCallExpression sys:
                if (!ValidateSysArity(sys.Name, sys.Arguments.Count, out error))
                {
                    type = MetaValueType.Int;
                    return false;
                }
                if (!ValidateSysArgTypes(sys.Name, sys.Arguments, ctx, out error))
                {
                    type = MetaValueType.Int;
                    return false;
                }
                type = InferSysReturnType(sys.Name);
                return true;
            default:
                type = MetaValueType.Int;
                error = "Compilation Error: Unsupported expression.";
                return false;
        }
    }

    private static MetaValueType InferSysReturnType(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "GET_TARGET" or "GET_SELF" or "GET_INTRUDER" => MetaValueType.Ptr,
            "GET_EVENT_SOURCE" or "FIND_NEAREST" => MetaValueType.Ptr,
            "GET_CONNECTED" or "GET_FILES" => MetaValueType.Arr,
            "GET_CLASS" => MetaValueType.Str,
            "GET_ICE" or "GET_TRACE" or "ARR_LENGTH" or "IS_VALID" => MetaValueType.Int,
            _ => MetaValueType.Int
        };
    }

    private static bool ValidateSysArity(string name, int count, out string? error)
    {
        if (!SysArity.TryGetValue(name, out var tuple))
        {
            error = $"Compilation Error: Unknown SYS call '{name}'.";
            return false;
        }

        if (count < tuple.Min || count > tuple.Max)
        {
            error = $"Compilation Error: SYS.{name} expects {tuple.Min}..{tuple.Max} args, got {count}.";
            return false;
        }

        error = null;
        return true;
    }

    private bool ValidateSysArgTypes(string name, IReadOnlyList<MetaExpression> args, ValidationContext ctx, out string? error)
    {
        error = null;
        var upper = name.ToUpperInvariant();
        switch (upper)
        {
            case "GET_CONNECTED":
            case "GET_CLASS":
            case "GET_ICE":
            case "PING":
            case "DISCONNECT":
            case "IS_VALID":
            case "GET_FILES":
                return args.Count > 0 && ExpectType(args[0], MetaValueType.Ptr, ctx, out error);
            case "INJECT":
                return args.Count > 1 && ExpectType(args[0], MetaValueType.Ptr, ctx, out error) && ExpectType(args[1], MetaValueType.Int, ctx, out error);
            case "CLOAK":
                return args.Count > 0 && ExpectType(args[0], MetaValueType.Int, ctx, out error);
            case "OVERRIDE":
                return args.Count > 2 && ExpectType(args[0], MetaValueType.Ptr, ctx, out error) && ExpectType(args[1], MetaValueType.Str, ctx, out error) && ExpectType(args[2], MetaValueType.Int, ctx, out error);
            case "BURN_NEUROPORT":
                return args.Count > 1 && ExpectType(args[0], MetaValueType.Ptr, ctx, out error) && ExpectType(args[1], MetaValueType.Int, ctx, out error);
            case "ARR_LENGTH":
                return args.Count > 0 && ExpectType(args[0], MetaValueType.Arr, ctx, out error);
            case "LOG":
                return args.Count > 0 && ExpectType(args[0], MetaValueType.Str, ctx, out error);
            case "FIND_NEAREST":
                return args.Count > 1 && ExpectType(args[0], MetaValueType.Str, ctx, out error) && ExpectType(args[1], MetaValueType.Int, ctx, out error);
            case "DOWNLOAD":
            case "UPLOAD":
                return args.Count > 1 && ExpectType(args[0], MetaValueType.Ptr, ctx, out error) && ExpectType(args[1], MetaValueType.Str, ctx, out error);
            default:
                return true;
        }
    }

    private static bool ContainsYield(List<MetaInstruction> body)
    {
        foreach (var i in body)
        {
            if (i is MetaYieldInstruction)
                return true;
            if (i is MetaIfInstruction ifi)
            {
                if (ContainsYield(ifi.ThenBody))
                    return true;
                if (ifi.ElseBody != null && ContainsYield(ifi.ElseBody))
                    return true;
            }
        }
        return false;
    }

    private static int EstimateRam(List<MetaInstruction> code)
    {
        var ram = 0;
        foreach (var instruction in code)
        {
            switch (instruction)
            {
                case MetaAllocRamInstruction a:
                    ram += Math.Max(0, a.Amount);
                    break;
                case MetaDefIntInstruction:
                case MetaDefStrInstruction:
                case MetaDefPtrInstruction:
                case MetaDefArrInstruction:
                    ram += 1;
                    break;
            }
        }
        return Math.Max(1, ram);
    }

    private sealed class ValidationContext
    {
        public readonly Dictionary<string, MetaValueType> Types = new();
        public int LoopDepth;

        public ValidationContext PushLoop(int loopDepth)
        {
            var ctx = new ValidationContext { LoopDepth = loopDepth };
            foreach (var (k, v) in Types)
                ctx.Types[k] = v;
            return ctx;
        }
    }
}
