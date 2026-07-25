using Content.Shared._NC.Netrunning.Meta;

namespace Content.Server._NC.Netrunning.Meta;

public sealed class MetaParser
{
    private readonly List<MetaToken> _tokens;
    private int _current;

    public MetaParser(List<MetaToken> tokens)
    {
        _tokens = tokens;
    }

    public List<MetaInstruction>? ParseProgram(out string? error, out bool requiresTarget)
    {
        error = null;
        requiresTarget = true;
        var result = new List<MetaInstruction>();

        try
        {
            if (MatchKeyword("REQUIRE"))
            {
                ConsumeKeyword("TARGET");
                Consume(MetaTokenType.LBracket, "Expected '[' after REQUIRE TARGET.");
                var value = RequireConstInt(ParseExpression(), "REQUIRE TARGET");
                Consume(MetaTokenType.RBracket, "Expected ']' after REQUIRE TARGET value.");
                ConsumeOptionalSemicolon();
                if (value is not 0 and not 1)
                    throw Error(Previous(), "REQUIRE TARGET accepts only 0 or 1.");

                requiresTarget = value == 1;
            }

            while (!IsAtEnd())
                result.Add(ParseInstruction());
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }

        return result;
    }

    private MetaInstruction ParseInstruction()
    {
        if (MatchKeyword("DEF"))
            return ParseDefinition(consumeSemicolon: true);

        if (MatchKeyword("IF"))
            return ParseIf();

        if (MatchKeyword("WHILE"))
            return ParseWhile();

        if (MatchKeyword("FOR"))
            return ParseFor();

        if (MatchKeyword("ON_EVENT"))
            return ParseOnEvent();

        if (MatchKeyword("BREAK"))
        {
            ConsumeOptionalSemicolon();
            return new MetaBreakInstruction();
        }

        if (MatchKeyword("CONTINUE"))
        {
            ConsumeOptionalSemicolon();
            return new MetaContinueInstruction();
        }

        if (MatchKeyword("YIELD"))
        {
            Consume(MetaTokenType.LBracket, "Expected '[' after YIELD.");
            var ms = ParseExpression();
            Consume(MetaTokenType.RBracket, "Expected ']'.");
            ConsumeOptionalSemicolon();
            return new MetaYieldInstruction(RequireConstInt(ms, "YIELD"));
        }

        if (MatchKeyword("EXIT"))
        {
            Consume(MetaTokenType.LBracket, "Expected '[' after EXIT.");
            var code = ParseExpression();
            Consume(MetaTokenType.RBracket, "Expected ']'.");
            ConsumeOptionalSemicolon();
            return new MetaExitInstruction(code);
        }

        if (CheckSysStart())
            return ParseSysStatement();

        if (CheckAssignmentStart())
        {
            var assign = ParseAssignmentStatement();
            ConsumeOptionalSemicolon();
            return assign;
        }

        throw Error(Peek(), $"Unexpected token '{Peek().Lexeme}'.");
    }

    private MetaInstruction ParseDefinition(bool consumeSemicolon)
    {
        var typeToken = Consume(MetaTokenType.Identifier, "Expected type after DEF.");
        var type = typeToken.Lexeme.ToUpperInvariant();
        var name = Consume(MetaTokenType.Identifier, "Expected variable name.").Lexeme;

        if (!Match(MetaTokenType.Assign))
        {
            if (type == "PTR")
            {
                if (consumeSemicolon)
                    ConsumeOptionalSemicolon();
                return new MetaDefPtrInstruction(name, new MetaIntLiteral(0));
            }

            throw Error(typeToken, "Only PTR can be declared without initializer.");
        }

        var expr = ParseExpression();
        if (consumeSemicolon)
            ConsumeOptionalSemicolon();

        return type switch
        {
            "INT" => new MetaDefIntInstruction(name, expr),
            "STR" => new MetaDefStrInstruction(name, expr),
            "PTR" => new MetaDefPtrInstruction(name, expr),
            "ARR" => new MetaDefArrInstruction(name, expr),
            _ => throw Error(typeToken, $"Unsupported DEF type '{typeToken.Lexeme}'.")
        };
    }

    private MetaInstruction ParseIf()
    {
        Consume(MetaTokenType.LParen, "Expected '(' after IF.");
        var cond = ParseExpression();
        Consume(MetaTokenType.RParen, "Expected ')' after IF condition.");
        var thenBody = ParseBlock();

        var elifs = new List<(MetaExpression Cond, List<MetaInstruction> Body)>();
        while (MatchKeyword("ELIF"))
        {
            Consume(MetaTokenType.LParen, "Expected '(' after ELIF.");
            var elifCond = ParseExpression();
            Consume(MetaTokenType.RParen, "Expected ')' after ELIF condition.");
            elifs.Add((elifCond, ParseBlock()));
        }

        List<MetaInstruction>? elseBody = null;
        if (MatchKeyword("ELSE"))
            elseBody = ParseBlock();

        List<MetaInstruction>? foldedElse = elseBody;
        for (var i = elifs.Count - 1; i >= 0; i--)
        {
            var nested = new MetaIfInstruction(elifs[i].Cond, elifs[i].Body, foldedElse);
            foldedElse = new List<MetaInstruction> { nested };
        }

        return new MetaIfInstruction(cond, thenBody, foldedElse);
    }

    private MetaInstruction ParseWhile()
    {
        Consume(MetaTokenType.LParen, "Expected '(' after WHILE.");
        var cond = ParseExpression();
        Consume(MetaTokenType.RParen, "Expected ')' after WHILE condition.");
        var body = ParseBlock();

        if (!HasYield(body))
            throw new Exception("Loop without YIELD detected. Hardware overheat risk. Every WHILE/FOR loop must contain at least one YIELD [ms] instruction.");

        return new MetaWhileInstruction(cond, body);
    }

    private MetaInstruction ParseFor()
    {
        Consume(MetaTokenType.LParen, "Expected '(' after FOR.");

        MetaInstruction? init = null;
        if (!Check(MetaTokenType.Semicolon))
            init = CheckKeyword("DEF") ? ParseDefForClause() : ParseAssignmentStatement();
        Consume(MetaTokenType.Semicolon, "Expected ';' after FOR init.");

        MetaExpression? condition = null;
        if (!Check(MetaTokenType.Semicolon))
            condition = ParseExpression();
        Consume(MetaTokenType.Semicolon, "Expected ';' after FOR condition.");

        MetaInstruction? step = null;
        if (!Check(MetaTokenType.RParen))
            step = ParseAssignmentStatement();
        Consume(MetaTokenType.RParen, "Expected ')' after FOR clauses.");

        var body = ParseBlock();

        if (!HasYield(body))
            throw new Exception("Loop without YIELD detected. Hardware overheat risk. Every WHILE/FOR loop must contain at least one YIELD [ms] instruction.");

        return new MetaForInstruction(init, condition, step, body);
    }

    private bool HasYield(List<MetaInstruction> body)
    {
        foreach (var inst in body)
        {
            if (inst is MetaYieldInstruction)
                return true;

            // Recurse into blocks (e.g. IF blocks)
            if (inst is MetaIfInstruction ifInst)
            {
                if (HasYield(ifInst.ThenBody)) return true;
                if (ifInst.ElseBody != null && HasYield(ifInst.ElseBody)) return true;
            }

            // Note: We don't recurse into nested WHILE/FOR because they must have their own YIELD.
            // But a YIELD inside a nested loop counts for that nested loop, not the outer one,
            // unless the outer one also hits it. 
            // Actually, if we have FOR { FOR { YIELD } }, the outer FOR's execution will eventually 
            // hit the inner's YIELD and suspend. So it is technically safe.
            if (inst is MetaWhileInstruction whileInst && HasYield(whileInst.Body)) return true;
            if (inst is MetaForInstruction forInst && HasYield(forInst.Body)) return true;
        }
        return false;
    }

    private MetaInstruction ParseOnEvent()
    {
        Consume(MetaTokenType.LParen, "Expected '(' after ON_EVENT.");
        var name = Consume(MetaTokenType.String, "Expected event name string.");
        Consume(MetaTokenType.RParen, "Expected ')' after ON_EVENT name.");
        return new MetaOnEventInstruction(name.Lexeme, ParseBlock());
    }

    private MetaInstruction ParseDefForClause()
    {
        ConsumeKeyword("DEF");
        return ParseDefinition(consumeSemicolon: false);
    }

    private MetaInstruction ParseSysStatement()
    {
        var call = ParseSysCall();
        ConsumeOptionalSemicolon();

        var name = call.Name.ToUpperInvariant();
        if (name == "LOG")
            return new MetaSysLogInstruction(call.Arguments[0]);
        if (name == "INJECT")
            return new MetaSysInjectInstruction(call.Arguments[0], call.Arguments[1]);
        if (name == "OVERRIDE")
            return new MetaSysOverrideInstruction(call.Arguments[0], call.Arguments[1], call.Arguments[2]);
        return new MetaSysSimpleInstruction(call.Name, call.Arguments);
    }

    private MetaInstruction ParseAssignmentStatement()
    {
        var name = Consume(MetaTokenType.Identifier, "Expected assignment target name.");

        if (Match(MetaTokenType.LBracket))
        {
            var index = ParseExpression();
            Consume(MetaTokenType.RBracket, "Expected ']' in array assignment target.");
            var op = ParseAssignOp();
            var value = ParseExpression();
            return new MetaAssignArrayInstruction(name.Lexeme, index, op, value);
        }

        var assignOp = ParseAssignOp();
        var expr = ParseExpression();
        return new MetaAssignInstruction(name.Lexeme, assignOp, expr);
    }

    private MetaAssignOp ParseAssignOp()
    {
        if (Match(MetaTokenType.Assign))
            return MetaAssignOp.Set;
        if (Match(MetaTokenType.PlusAssign))
            return MetaAssignOp.AddAssign;
        if (Match(MetaTokenType.MinusAssign))
            return MetaAssignOp.SubAssign;
        throw Error(Peek(), "Expected assignment operator (=, +=, -=).");
    }

    private List<MetaInstruction> ParseBlock()
    {
        Consume(MetaTokenType.LBrace, "Expected '{' to start block.");
        var body = new List<MetaInstruction>();
        while (!Check(MetaTokenType.RBrace) && !IsAtEnd())
            body.Add(ParseInstruction());
        Consume(MetaTokenType.RBrace, "Expected '}' after block.");
        return body;
    }

    private MetaExpression ParseExpression() => ParseLogicOr();
    private MetaExpression ParseLogicOr()
    {
        var expr = ParseLogicAnd();
        while (Match(MetaTokenType.OrOr))
            expr = new MetaBinaryExpression(expr, MetaBinaryOp.Or, ParseLogicAnd());
        return expr;
    }

    private MetaExpression ParseLogicAnd()
    {
        var expr = ParseEquality();
        while (Match(MetaTokenType.AndAnd))
            expr = new MetaBinaryExpression(expr, MetaBinaryOp.And, ParseEquality());
        return expr;
    }

    private MetaExpression ParseEquality()
    {
        var expr = ParseComparison();
        while (Match(MetaTokenType.Equals) || Match(MetaTokenType.NotEquals))
        {
            var op = Previous().Type == MetaTokenType.Equals ? MetaBinaryOp.Equals : MetaBinaryOp.NotEquals;
            expr = new MetaBinaryExpression(expr, op, ParseComparison());
        }
        return expr;
    }

    private MetaExpression ParseComparison()
    {
        var expr = ParseTerm();
        while (Match(MetaTokenType.Less) || Match(MetaTokenType.LessOrEqual) ||
               Match(MetaTokenType.Greater) || Match(MetaTokenType.GreaterOrEqual))
        {
            var op = Previous().Type switch
            {
                MetaTokenType.Less => MetaBinaryOp.Less,
                MetaTokenType.LessOrEqual => MetaBinaryOp.LessOrEqual,
                MetaTokenType.Greater => MetaBinaryOp.Greater,
                _ => MetaBinaryOp.GreaterOrEqual
            };
            expr = new MetaBinaryExpression(expr, op, ParseTerm());
        }
        return expr;
    }

    private MetaExpression ParseTerm()
    {
        var expr = ParseFactor();
        while (Match(MetaTokenType.Plus) || Match(MetaTokenType.Minus))
        {
            var op = Previous().Type == MetaTokenType.Plus ? MetaBinaryOp.Add : MetaBinaryOp.Subtract;
            expr = new MetaBinaryExpression(expr, op, ParseFactor());
        }
        return expr;
    }

    private MetaExpression ParseFactor()
    {
        var expr = ParseUnary();
        while (Match(MetaTokenType.Star) || Match(MetaTokenType.Slash) || Match(MetaTokenType.Percent))
        {
            var op = Previous().Type switch
            {
                MetaTokenType.Star => MetaBinaryOp.Multiply,
                MetaTokenType.Slash => MetaBinaryOp.Divide,
                _ => MetaBinaryOp.Modulo
            };
            expr = new MetaBinaryExpression(expr, op, ParseUnary());
        }
        return expr;
    }

    private MetaExpression ParseUnary()
    {
        if (Match(MetaTokenType.Bang))
            return new MetaUnaryExpression(MetaUnaryOp.Not, ParseUnary());
        if (Match(MetaTokenType.Minus))
            return new MetaUnaryExpression(MetaUnaryOp.Negate, ParseUnary());
        return ParsePrimary();
    }

    private MetaExpression ParsePrimary()
    {
        if (Match(MetaTokenType.Number))
            return new MetaIntLiteral(int.Parse(Previous().Lexeme));
        if (Match(MetaTokenType.String))
            return new MetaStringLiteral(Previous().Lexeme);
        if (Match(MetaTokenType.LParen))
        {
            var inner = ParseExpression();
            Consume(MetaTokenType.RParen, "Expected ')' after expression.");
            return inner;
        }

        if (CheckSysStart())
            return ParseSysCall();

        if (Match(MetaTokenType.Identifier))
        {
            var name = Previous().Lexeme;
            if (Match(MetaTokenType.LBracket))
            {
                var idx = ParseExpression();
                Consume(MetaTokenType.RBracket, "Expected ']' after array index.");
                return new MetaArrayIndexExpression(name, idx);
            }

            return new MetaVariableExpression(name);
        }

        throw Error(Peek(), $"Unexpected token in expression '{Peek().Lexeme}'.");
    }

    private MetaSysCallExpression ParseSysCall()
    {
        ConsumeKeyword("SYS");
        Consume(MetaTokenType.Dot, "Expected '.' after SYS.");
        var method = Consume(MetaTokenType.Identifier, "Expected SYS method name.");
        Consume(MetaTokenType.LParen, "Expected '(' after SYS method.");

        var args = new List<MetaExpression>();
        if (!Check(MetaTokenType.RParen))
        {
            do
            {
                args.Add(ParseExpression());
            } while (Match(MetaTokenType.Comma));
        }

        Consume(MetaTokenType.RParen, "Expected ')' after SYS call.");
        return new MetaSysCallExpression(method.Lexeme, args);
    }

    private bool CheckAssignmentStart()
    {
        if (!Check(MetaTokenType.Identifier))
            return false;
        var next = PeekNext().Type;
        return next is MetaTokenType.Assign or MetaTokenType.PlusAssign or MetaTokenType.MinusAssign or MetaTokenType.LBracket;
    }

    private bool CheckSysStart()
    {
        return CheckKeyword("SYS") && PeekNext().Type == MetaTokenType.Dot;
    }

    private bool MatchKeyword(string keyword)
    {
        if (!CheckKeyword(keyword))
            return false;
        Advance();
        return true;
    }

    private bool CheckKeyword(string keyword)
    {
        return Check(MetaTokenType.Identifier) && Peek().Lexeme.Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void ConsumeKeyword(string keyword)
    {
        var token = Consume(MetaTokenType.Identifier, $"Expected keyword '{keyword}'.");
        if (!token.Lexeme.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            throw Error(token, $"Expected keyword '{keyword}'.");
    }

    private int RequireConstInt(MetaExpression expr, string ctx)
    {
        if (expr is not MetaIntLiteral literal)
            throw Error(Peek(), $"{ctx} requires constant INT literal.");
        return literal.Value;
    }

    private MetaToken Consume(MetaTokenType type, string message)
    {
        if (Check(type))
            return Advance();
        throw Error(Peek(), message);
    }

    private void ConsumeOptionalSemicolon()
    {
        if (Match(MetaTokenType.Semicolon))
            return;
    }

    private bool Match(MetaTokenType type)
    {
        if (!Check(type))
            return false;
        Advance();
        return true;
    }

    private bool Check(MetaTokenType type) => !IsAtEnd() && Peek().Type == type;
    private bool IsAtEnd() => Peek().Type == MetaTokenType.EndOfFile;
    private MetaToken Advance() => _tokens[_current++];
    private MetaToken Peek() => _tokens[_current];
    private MetaToken PeekNext() => _current + 1 < _tokens.Count ? _tokens[_current + 1] : _tokens[^1];
    private MetaToken Previous() => _tokens[_current - 1];

    private Exception Error(MetaToken token, string message)
    {
        return new Exception($"META parse error at {token.Line}:{token.Column}. {message}");
    }
}
