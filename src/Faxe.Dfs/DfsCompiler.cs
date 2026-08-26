using System.Globalization;
using Faxe.Core.Models;

namespace Faxe.Dfs;

/// <summary>
/// Compiles DFS scripts into graph definitions (nodes + edges), matching faxe_dfs behaviour
/// for the common pipeline / def / option forms.
/// </summary>
public sealed class DfsCompiler
{
    private int _nodeSeq;
    private readonly HashSet<string>? _knownNodes;

    public DfsCompiler(IEnumerable<string>? knownNodeNames = null)
    {
        _knownNodes = knownNodeNames is null
            ? null
            : new HashSet<string>(knownNodeNames, StringComparer.Ordinal);
    }

    public GraphDefinition Compile(string script, IReadOnlyDictionary<string, object?>? vars = null)
    {
        var tokens = DfsLexer.Tokenize(script);
        var parser = new DfsParser(tokens, vars ?? new Dictionary<string, object?>());
        var program = parser.Parse();
        return BuildGraph(program);
    }

    public (bool Ok, string? Error, GraphDefinition? Graph) TryCompile(string script, IReadOnlyDictionary<string, object?>? vars = null)
    {
        try
        {
            return (true, null, Compile(script, vars));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    private GraphDefinition BuildGraph(DfsProgram program)
    {
        _nodeSeq = 0;
        var def = new GraphDefinition();
        string? lastNodeId = null;

        void EmitChain(IReadOnlyList<DfsNodeCall> chain, string? seedLast)
        {
            var prev = seedLast;
            foreach (var call in chain)
            {
                if (_knownNodes is not null && !_knownNodes.Contains(call.Name))
                    throw new DfsException($"Component '{call.Name}' not found");

                var id = NextId(call.Name);
                var opts = new Dictionary<string, object?>(call.Options, StringComparer.Ordinal);
                // Positional params stored under special key for later binding
                if (call.Positional.Count > 0)
                    opts["__positional"] = call.Positional.ToList();

                def.Nodes.Add(new GraphNodeDef
                {
                    Name = id,
                    Type = call.Name,
                    Options = opts
                });

                if (prev is not null)
                {
                    def.Edges.Add(new GraphEdgeDef
                    {
                        Source = prev,
                        OutPort = 1,
                        Dest = id,
                        InPort = 1
                    });
                }
                prev = id;
            }
            lastNodeId = prev;
        }

        foreach (var stmt in program.Statements)
        {
            switch (stmt)
            {
                case DfsChainStatement chain:
                    EmitChain(chain.Nodes, null);
                    break;
                case DfsDefStatement d:
                    if (d.Chain is { Count: > 0 })
                        EmitChain(d.Chain, null);
                    break;
                case DfsIdentChainStatement ident:
                    EmitChain(ident.Chain, lastNodeId);
                    break;
            }
        }

        return def;
    }

    private string NextId(string type)
    {
        _nodeSeq++;
        return type + _nodeSeq.ToString(CultureInfo.InvariantCulture);
    }
}

internal sealed class DfsProgram
{
    public List<DfsStatement> Statements { get; } = new();
}

internal abstract record DfsStatement;
internal sealed record DfsChainStatement(List<DfsNodeCall> Nodes) : DfsStatement;
internal sealed record DfsDefStatement(string Name, List<DfsNodeCall>? Chain, object? Value) : DfsStatement;
internal sealed record DfsIdentChainStatement(string Ident, List<DfsNodeCall> Chain) : DfsStatement;

internal sealed class DfsNodeCall
{
    public string Name { get; init; } = "";
    public List<object?> Positional { get; } = new();
    public Dictionary<string, object?> Options { get; } = new(StringComparer.Ordinal);
}

internal sealed class DfsParser
{
    private readonly List<Token> _tokens;
    private readonly Dictionary<string, object?> _vars;
    private int _pos;

    public DfsParser(List<Token> tokens, IReadOnlyDictionary<string, object?> vars)
    {
        _tokens = tokens;
        _vars = new Dictionary<string, object?>(vars, StringComparer.Ordinal);
    }

    public DfsProgram Parse()
    {
        var program = new DfsProgram();
        while (!Check(TokenKind.Eof))
        {
            if (Check(TokenKind.Def))
                program.Statements.Add(ParseDef());
            else if (Check(TokenKind.Node) || Check(TokenKind.Macro) || Check(TokenKind.UserNode))
                program.Statements.Add(new DfsChainStatement(ParseChain()));
            else if (Check(TokenKind.Identifier))
            {
                var ident = Advance().Value;
                if (Check(TokenKind.Node) || Check(TokenKind.Dot) || Check(TokenKind.Macro) || Check(TokenKind.UserNode))
                    program.Statements.Add(new DfsIdentChainStatement(ident, ParseChain()));
                else
                    throw new DfsException($"Unexpected identifier '{ident}' at line {Peek().Line}");
            }
            else
                throw new DfsException($"Unexpected token {Peek().Kind} '{Peek().Value}' at line {Peek().Line}");
        }
        return program;
    }

    private DfsDefStatement ParseDef()
    {
        Expect(TokenKind.Def);
        var name = Expect(TokenKind.Identifier).Value;
        Expect(TokenKind.Eq);
        if (Check(TokenKind.Node) || Check(TokenKind.Macro) || Check(TokenKind.UserNode))
        {
            var chain = ParseChain();
            _vars[name] = chain;
            return new DfsDefStatement(name, chain, null);
        }

        var value = ParseValue();
        _vars[name] = value;
        return new DfsDefStatement(name, null, value);
    }

    private List<DfsNodeCall> ParseChain()
    {
        var nodes = new List<DfsNodeCall>();
        while (Check(TokenKind.Node) || Check(TokenKind.Macro) || Check(TokenKind.UserNode) ||
               (Check(TokenKind.Dot) && nodes.Count > 0))
        {
            if (Check(TokenKind.Dot))
            {
                Advance();
                var optName = Expect(TokenKind.Identifier).Value;
                Expect(TokenKind.LParen);
                var args = ParseArgList();
                Expect(TokenKind.RParen);
                if (nodes.Count == 0)
                    throw new DfsException($"Option '{optName}' without node at line {Peek().Line}");
                nodes[^1].Options[optName] = args.Count == 1 ? args[0] : args;
                continue;
            }

            var kind = Advance().Kind;
            var nname = Expect(TokenKind.Identifier).Value;
            if (kind == TokenKind.UserNode)
                nname = "python3";
            Expect(TokenKind.LParen);
            var positional = ParseArgList();
            Expect(TokenKind.RParen);
            var call = new DfsNodeCall { Name = nname };
            call.Positional.AddRange(positional);
            nodes.Add(call);

            while (Check(TokenKind.Dot))
            {
                Advance();
                var optName = Expect(TokenKind.Identifier).Value;
                Expect(TokenKind.LParen);
                var args = ParseArgList();
                Expect(TokenKind.RParen);
                call.Options[optName] = args.Count == 1 ? args[0] : args;
            }
        }
        return nodes;
    }

    private List<object?> ParseArgList()
    {
        var args = new List<object?>();
        if (Check(TokenKind.RParen) || Check(TokenKind.RBracket) || Check(TokenKind.RBrace))
            return args;
        args.Add(ParseValue());
        while (Check(TokenKind.Comma))
        {
            Advance();
            args.Add(ParseValue());
        }
        return args;
    }

    private object? ParseValue()
    {
        // unary minus before number/duration
        if (Check(TokenKind.Operator) && Peek().Value == "-")
        {
            Advance();
            var inner = ParseValue();
            return inner switch
            {
                long l => -l,
                double d => -d,
                string s when s.Length > 0 && char.IsDigit(s[0]) => "-" + s, // duration like 3m -> -3m
                _ => inner
            };
        }

        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.String:
            case TokenKind.Text:
            case TokenKind.Duration:
            case TokenKind.Reference:
                Advance();
                return t.Value;
            case TokenKind.Lambda:
                Advance();
                return new LambdaExpression(t.Value);
            case TokenKind.Bool:
                Advance();
                return t.Value == "true";
            case TokenKind.Int:
                Advance();
                return long.Parse(t.Value, CultureInfo.InvariantCulture);
            case TokenKind.Float:
                Advance();
                return double.Parse(t.Value, CultureInfo.InvariantCulture);
            case TokenKind.Identifier:
                Advance();
                // function-call style value: name(args)  (inline e: expr left as identifier for now)
                if (Check(TokenKind.LParen))
                {
                    Advance();
                    var callArgs = ParseArgList();
                    Expect(TokenKind.RParen);
                    return new DfsFuncCall(t.Value, callArgs);
                }
                if (Check(TokenKind.Colon))
                {
                    // e: env(...) style inline — consume ':' and following value as opaque expression marker
                    Advance();
                    var expr = ParseValue();
                    return new DfsInlineExpr(t.Value, expr);
                }
                if (_vars.TryGetValue(t.Value, out var v) && v is not List<DfsNodeCall>)
                    return v;
                return t.Value;
            case TokenKind.Inline:
                Advance();
                return new DfsInlineExpr("e", t.Value);
            case TokenKind.LBracket:
                Advance();
                var list = ParseArgList();
                Expect(TokenKind.RBracket);
                return list;
            case TokenKind.LBrace:
                Advance();
                var tuple = ParseArgList();
                Expect(TokenKind.RBrace);
                return tuple;
            default:
                throw new DfsException($"Unexpected value token {t.Kind} at line {t.Line}");
        }
    }

    private Token Peek() => _tokens[_pos];
    private bool Check(TokenKind kind) => Peek().Kind == kind;
    private Token Advance() => _tokens[_pos++];
    private Token Expect(TokenKind kind)
    {
        if (!Check(kind))
            throw new DfsException($"Expected {kind} but got {Peek().Kind} '{Peek().Value}' at line {Peek().Line}");
        return Advance();
    }
}

public sealed class LambdaExpression
{
    public LambdaExpression(string body) => Body = body;
    public string Body { get; }
}

public sealed class DfsFuncCall
{
    public DfsFuncCall(string name, List<object?> args)
    {
        Name = name;
        Args = args;
    }
    public string Name { get; }
    public List<object?> Args { get; }
}

public sealed class DfsInlineExpr
{
    public DfsInlineExpr(string kind, object? body)
    {
        Kind = kind;
        Body = body;
    }
    public string Kind { get; }
    public object? Body { get; }
}
