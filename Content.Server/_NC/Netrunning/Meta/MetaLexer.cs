using System.Globalization;

namespace Content.Server._NC.Netrunning.Meta;

public enum MetaTokenType : byte
{
    Identifier,
    Number,
    String,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Semicolon,
    Comma,
    Dot,
    Assign,
    Plus,
    PlusAssign,
    Minus,
    MinusAssign,
    Star,
    Slash,
    Percent,
    Bang,
    AndAnd,
    OrOr,
    Equals,
    NotEquals,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    EndOfFile,
}

public readonly record struct MetaToken(MetaTokenType Type, string Lexeme, int Line, int Column);

public sealed class MetaLexer
{
    private readonly string _source;
    private readonly List<MetaToken> _tokens = new();
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public MetaLexer(string source)
    {
        _source = source;
    }

    public List<MetaToken> Tokenize(out string? error)
    {
        error = null;

        while (!IsAtEnd())
        {
            var startLine = _line;
            var startColumn = _column;
            var c = Advance();

            switch (c)
            {
                case ' ':
                case '\t':
                case '\r':
                    break;
                case '\n':
                    _line++;
                    _column = 1;
                    break;
                case '(':
                    Add(MetaTokenType.LParen, "(", startLine, startColumn);
                    break;
                case ')':
                    Add(MetaTokenType.RParen, ")", startLine, startColumn);
                    break;
                case '{':
                    Add(MetaTokenType.LBrace, "{", startLine, startColumn);
                    break;
                case '}':
                    Add(MetaTokenType.RBrace, "}", startLine, startColumn);
                    break;
                case '[':
                    Add(MetaTokenType.LBracket, "[", startLine, startColumn);
                    break;
                case ']':
                    Add(MetaTokenType.RBracket, "]", startLine, startColumn);
                    break;
                case ';':
                    Add(MetaTokenType.Semicolon, ";", startLine, startColumn);
                    break;
                case ',':
                    Add(MetaTokenType.Comma, ",", startLine, startColumn);
                    break;
                case '.':
                    Add(MetaTokenType.Dot, ".", startLine, startColumn);
                    break;
                case '+':
                    if (Match('='))
                        Add(MetaTokenType.PlusAssign, "+=", startLine, startColumn);
                    else
                        Add(MetaTokenType.Plus, "+", startLine, startColumn);
                    break;
                case '-':
                    if (Match('='))
                        Add(MetaTokenType.MinusAssign, "-=", startLine, startColumn);
                    else
                        Add(MetaTokenType.Minus, "-", startLine, startColumn);
                    break;
                case '*':
                    Add(MetaTokenType.Star, "*", startLine, startColumn);
                    break;
                case '/':
                    if (Match('/'))
                    {
                        while (!IsAtEnd() && Peek() != '\n') Advance();
                    }
                    else
                    {
                        Add(MetaTokenType.Slash, "/", startLine, startColumn);
                    }
                    break;
                case '%':
                    Add(MetaTokenType.Percent, "%", startLine, startColumn);
                    break;
                case '=':
                    if (Match('='))
                        Add(MetaTokenType.Equals, "==", startLine, startColumn);
                    else
                        Add(MetaTokenType.Assign, "=", startLine, startColumn);
                    break;
                case '!':
                    if (Match('='))
                        Add(MetaTokenType.NotEquals, "!=", startLine, startColumn);
                    else
                        Add(MetaTokenType.Bang, "!", startLine, startColumn);
                    break;
                case '&':
                    if (!Match('&'))
                    {
                        error = $"Unexpected '&' at {startLine}:{startColumn}";
                        return _tokens;
                    }
                    Add(MetaTokenType.AndAnd, "&&", startLine, startColumn);
                    break;
                case '|':
                    if (!Match('|'))
                    {
                        error = $"Unexpected '|' at {startLine}:{startColumn}";
                        return _tokens;
                    }
                    Add(MetaTokenType.OrOr, "||", startLine, startColumn);
                    break;
                case '<':
                    if (Match('='))
                        Add(MetaTokenType.LessOrEqual, "<=", startLine, startColumn);
                    else
                        Add(MetaTokenType.Less, "<", startLine, startColumn);
                    break;
                case '>':
                    if (Match('='))
                        Add(MetaTokenType.GreaterOrEqual, ">=", startLine, startColumn);
                    else
                        Add(MetaTokenType.Greater, ">", startLine, startColumn);
                    break;
                case '"':
                    if (!ReadString(startLine, startColumn, out error))
                        return _tokens;
                    break;
                default:
                    if (char.IsDigit(c))
                    {
                        ReadNumber(c, startLine, startColumn);
                        break;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        ReadIdentifier(c, startLine, startColumn);
                        break;
                    }

                    error = $"Unexpected character '{c}' at {startLine}:{startColumn}";
                    return _tokens;
            }
        }

        _tokens.Add(new MetaToken(MetaTokenType.EndOfFile, string.Empty, _line, _column));
        return _tokens;
    }

    private void Add(MetaTokenType type, string lexeme, int line, int column)
    {
        _tokens.Add(new MetaToken(type, lexeme, line, column));
    }

    private bool ReadString(int line, int column, out string? error)
    {
        error = null;
        var start = _index;

        while (!IsAtEnd() && Peek() != '"')
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            Advance();
        }

        if (IsAtEnd())
        {
            error = $"Unterminated string at {line}:{column}";
            return false;
        }

        Advance(); // closing quote
        var value = _source[start..(_index - 1)];
        Add(MetaTokenType.String, value, line, column);
        return true;
    }

    private void ReadNumber(char first, int line, int column)
    {
        var text = first.ToString(CultureInfo.InvariantCulture);
        while (!IsAtEnd() && char.IsDigit(Peek()))
            text += Advance();

        Add(MetaTokenType.Number, text, line, column);
    }

    private void ReadIdentifier(char first, int line, int column)
    {
        var text = first.ToString(CultureInfo.InvariantCulture);
        while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            text += Advance();

        Add(MetaTokenType.Identifier, text, line, column);
    }

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_index] != expected)
            return false;

        _index++;
        _column++;
        return true;
    }

    private char Peek() => _source[_index];

    private char Advance()
    {
        var c = _source[_index++];
        _column++;
        return c;
    }

    private bool IsAtEnd() => _index >= _source.Length;
}
