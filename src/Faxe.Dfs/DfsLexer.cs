using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Faxe.Dfs;

public enum TokenKind
{
    Def, Node, Macro, UserNode, Identifier, String, Text, Reference,
    Int, Float, Duration, Bool, Lambda, Inline, Operator,
    LParen, RParen, LBracket, RBracket, LBrace, RBrace,
    Dot, Comma, Eq, Colon, Eof
}

public readonly record struct Token(TokenKind Kind, string Value, int Line);

public static partial class DfsLexer
{
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '%')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (source.AsSpan(i).StartsWith("lambda:", StringComparison.Ordinal))
            {
                i += 7;
                while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
                var start = i;
                // lambda body until we hit a top-level terminator for param lists: , ) at paren depth 0 of lambda... 
                // For simplicity capture until matching end of argument: look for next option boundary carefully.
                // Take until we see "," or ")" at depth 0 of (),[],{} and not inside quotes.
                i = ScanLambdaBody(source, i);
                tokens.Add(new Token(TokenKind.Lambda, source[start..i].Trim(), line));
                continue;
            }

            if (source.AsSpan(i).StartsWith("<<<", StringComparison.Ordinal))
            {
                var end = source.IndexOf(">>>", i + 3, StringComparison.Ordinal);
                if (end < 0) throw new DfsException($"Unclosed text literal at line {line}");
                var text = source[(i + 3)..end].Trim();
                tokens.Add(new Token(TokenKind.Text, text, line));
                i = end + 3;
                continue;
            }

            if (c == '|')
            {
                if (i + 1 < source.Length && source[i + 1] == '|')
                {
                    tokens.Add(new Token(TokenKind.Macro, "||", line));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Node, "|", line));
                    i++;
                }
                continue;
            }

            if (c == '@') { tokens.Add(new Token(TokenKind.UserNode, "@", line)); i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(", line)); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")", line)); i++; continue; }
            if (c == '[') { tokens.Add(new Token(TokenKind.LBracket, "[", line)); i++; continue; }
            if (c == ']') { tokens.Add(new Token(TokenKind.RBracket, "]", line)); i++; continue; }
            if (c == '{') { tokens.Add(new Token(TokenKind.LBrace, "{", line)); i++; continue; }
            if (c == '}') { tokens.Add(new Token(TokenKind.RBrace, "}", line)); i++; continue; }
            if (c == '.') { tokens.Add(new Token(TokenKind.Dot, ".", line)); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",", line)); i++; continue; }
            if (c == '=')
            {
                if (i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.Operator, "==", line));
                    i += 2;
                }
                else if (i + 1 < source.Length && source[i + 1] == '~')
                {
                    tokens.Add(new Token(TokenKind.Operator, "=~", line));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Eq, "=", line));
                    i++;
                }
                continue;
            }
            if (c == ':') { tokens.Add(new Token(TokenKind.Colon, ":", line)); i++; continue; }

            if (c == '\'' )
            {
                i++;
                var sb = new StringBuilder();
                while (i < source.Length)
                {
                    if (source[i] == '\'' && i + 1 < source.Length && source[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                        continue;
                    }
                    if (source[i] == '\'') { i++; break; }
                    sb.Append(source[i++]);
                }
                tokens.Add(new Token(TokenKind.String, sb.ToString(), line));
                continue;
            }

            if (c == '"')
            {
                i++;
                var start = i;
                while (i < source.Length && source[i] != '"') i++;
                tokens.Add(new Token(TokenKind.Reference, source[start..i], line));
                if (i < source.Length) i++;
                continue;
            }

            var opMatch = OperatorRegex().Match(source, i);
            if (opMatch.Success && opMatch.Index == i)
            {
                tokens.Add(new Token(TokenKind.Operator, opMatch.Value, line));
                i += opMatch.Length;
                continue;
            }

            var dur = DurationRegex().Match(source, i);
            if (dur.Success && dur.Index == i)
            {
                tokens.Add(new Token(TokenKind.Duration, dur.Value, line));
                i += dur.Length;
                continue;
            }

            var flt = FloatRegex().Match(source, i);
            if (flt.Success && flt.Index == i)
            {
                tokens.Add(new Token(TokenKind.Float, flt.Value, line));
                i += flt.Length;
                continue;
            }

            var integ = IntRegex().Match(source, i);
            if (integ.Success && integ.Index == i)
            {
                tokens.Add(new Token(TokenKind.Int, integ.Value, line));
                i += integ.Length;
                continue;
            }

            var id = IdentRegex().Match(source, i);
            if (id.Success && id.Index == i)
            {
                var v = id.Value;
                if (v.Equals("def", StringComparison.Ordinal) || v.Equals("var", StringComparison.Ordinal))
                    tokens.Add(new Token(TokenKind.Def, v, line));
                else if (v.Equals("true", StringComparison.OrdinalIgnoreCase))
                    tokens.Add(new Token(TokenKind.Bool, "true", line));
                else if (v.Equals("false", StringComparison.OrdinalIgnoreCase))
                    tokens.Add(new Token(TokenKind.Bool, "false", line));
                else
                    tokens.Add(new Token(TokenKind.Identifier, v, line));
                i += id.Length;
                continue;
            }

            throw new DfsException($"Unexpected character '{c}' at line {line}");
        }

        tokens.Add(new Token(TokenKind.Eof, "", line));
        return tokens;
    }

    private static int ScanLambdaBody(string source, int i)
    {
        var depthParen = 0;
        var depthBracket = 0;
        var depthBrace = 0;
        var inStr = false;
        var inRef = false;
        while (i < source.Length)
        {
            var c = source[i];
            if (inStr)
            {
                if (c == '\'' && i + 1 < source.Length && source[i + 1] == '\'') { i += 2; continue; }
                if (c == '\'') inStr = false;
                i++;
                continue;
            }
            if (inRef)
            {
                if (c == '"') inRef = false;
                i++;
                continue;
            }
            if (c == '\'') { inStr = true; i++; continue; }
            if (c == '"') { inRef = true; i++; continue; }
            if (c == '(') { depthParen++; i++; continue; }
            if (c == ')')
            {
                if (depthParen == 0 && depthBracket == 0 && depthBrace == 0) break;
                depthParen--; i++; continue;
            }
            if (c == '[') { depthBracket++; i++; continue; }
            if (c == ']') { depthBracket--; i++; continue; }
            if (c == '{') { depthBrace++; i++; continue; }
            if (c == '}') { depthBrace--; i++; continue; }
            if (c == ',' && depthParen == 0 && depthBracket == 0 && depthBrace == 0) break;
            if (c == '\n' && depthParen == 0 && depthBracket == 0 && depthBrace == 0)
            {
                // allow multiline lambdas; keep going unless next non-ws starts a new statement-ish token
                var j = i + 1;
                while (j < source.Length && (source[j] == ' ' || source[j] == '\t')) j++;
                if (j < source.Length &&
                    ((source[j] is '|' or '%') ||
                     source.AsSpan(j).StartsWith("def", StringComparison.Ordinal)))
                    break;
            }
            i++;
        }
        return i;
    }

    [GeneratedRegex(@"!=|!~|<=|>=|==|=~|\+|-|\*|/|<|>|!")]
    private static partial Regex OperatorRegex();
    [GeneratedRegex(@"[+-]?\d+(ms|s|m|h|d|w)")]
    private static partial Regex DurationRegex();
    [GeneratedRegex(@"[+-]?\d+\.\d+")]
    private static partial Regex FloatRegex();
    [GeneratedRegex(@"[+-]?\d+")]
    private static partial Regex IntRegex();
    [GeneratedRegex(@"[a-z_][0-9a-zA-Z_\.]*", RegexOptions.IgnoreCase)]
    private static partial Regex IdentRegex();
}

public sealed class DfsException : Exception
{
    public DfsException(string message) : base(message) { }
}
