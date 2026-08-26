using System.Globalization;
using System.Text.RegularExpressions;
using Faxe.Core;
using Faxe.Core.Data;

namespace Faxe.Dfs;

/// <summary>
/// Evaluates Faxe lambda expressions against a data_point.
/// Supports field refs ("path"), arithmetic, comparisons, and common helpers (round, bool).
/// </summary>
public static partial class LambdaEval
{
    public static object? Execute(DataPoint point, string expression)
    {
        var expr = expression.Trim();
        // Replace "field.path" references with JSON-ish placeholders evaluated via FlowData
        expr = FieldRefRegex().Replace(expr, m =>
        {
            var path = m.Groups[1].Value;
            var val = FlowData.Get(point, path);
            return Literal(val);
        });

        return EvalSimple(expr);
    }

    public static bool ExecuteBool(DataPoint point, string expression) =>
        ToBool(Execute(point, expression));

    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s) && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
        IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture) != 0,
        _ => true
    };

    private static string Literal(object? val) => val switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => "\"" + s.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        IFormattable f => Convert.ToString(f, CultureInfo.InvariantCulture) ?? "null",
        _ => "null"
    };

    private static object? EvalSimple(string expr)
    {
        expr = expr.Trim();
        if (expr == "null") return null;
        if (expr == "true") return true;
        if (expr == "false") return false;

        var round = RoundRegex().Match(expr);
        if (round.Success)
            return Math.Round(Convert.ToDouble(EvalSimple(round.Groups[1].Value), CultureInfo.InvariantCulture));

        // Comparisons
        foreach (var op in new[] { ">=", "<=", "!=", "==", ">", "<" })
        {
            var idx = IndexOfOp(expr, op);
            if (idx > 0)
            {
                var left = EvalSimple(expr[..idx]);
                var right = EvalSimple(expr[(idx + op.Length)..]);
                var cmp = Compare(left, right);
                return op switch
                {
                    "==" => cmp == 0,
                    "!=" => cmp != 0,
                    ">" => cmp > 0,
                    "<" => cmp < 0,
                    ">=" => cmp >= 0,
                    "<=" => cmp <= 0,
                    _ => false
                };
            }
        }

        foreach (var op in new[] { "+", "-" })
        {
            var idx = IndexOfOp(expr, op);
            if (idx > 0)
            {
                var left = Convert.ToDouble(EvalSimple(expr[..idx]), CultureInfo.InvariantCulture);
                var right = Convert.ToDouble(EvalSimple(expr[(idx + 1)..]), CultureInfo.InvariantCulture);
                return op == "+" ? left + right : left - right;
            }
        }

        foreach (var op in new[] { "*", "/" })
        {
            var idx = IndexOfOp(expr, op);
            if (idx > 0)
            {
                var left = Convert.ToDouble(EvalSimple(expr[..idx]), CultureInfo.InvariantCulture);
                var right = Convert.ToDouble(EvalSimple(expr[(idx + 1)..]), CultureInfo.InvariantCulture);
                return op == "*" ? left * right : left / right;
            }
        }

        if (expr.StartsWith('"') && expr.EndsWith('"'))
            return expr[1..^1];

        if (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return num;

        return expr;
    }

    private static int Compare(object? left, object? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        if (left is IComparable && right is IComparable &&
            double.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
            double.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
            return l.CompareTo(r);
        return string.Compare(Convert.ToString(left), Convert.ToString(right), StringComparison.Ordinal);
    }

    private static int IndexOfOp(string expr, string op)
    {
        var depth = 0;
        var inStr = false;
        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];
            if (c == '"') inStr = !inStr;
            if (inStr) continue;
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && expr.AsSpan(i).StartsWith(op, StringComparison.Ordinal))
            {
                // avoid matching unary +/-
                if (op is "+" or "-" && i == 0) continue;
                return i;
            }
        }
        return -1;
    }

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex FieldRefRegex();

    [GeneratedRegex(@"^round\((.+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RoundRegex();
}
