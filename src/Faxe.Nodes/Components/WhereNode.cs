using Faxe.Core.Data;
using Faxe.Dfs;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_where — filter by lambda boolean.</summary>
[FaxeNode("where")]
public sealed class WhereNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("lambda", NodeOptionType.Lambda),
        NodeOption.Define("emit_empty", NodeOptionType.Boolean, false)
    };

    private string? _lambda;
    private bool _emitEmpty;

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _lambda = options.TryGetValue("lambda", out var l)
            ? l switch { LambdaExpression le => le.Body, _ => Convert.ToString(l) }
            : null;
        _emitEmpty = Opt(options, "emit_empty", false);
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_lambda))
            return Task.FromResult(NodeResult.Emit(item));

        switch (item)
        {
            case DataItem.Point p:
                return Task.FromResult(LambdaEval.ExecuteBool(p.Value, _lambda)
                    ? NodeResult.Emit(item)
                    : NodeResult.OkResult());
            case DataItem.Batch b:
            {
                var kept = b.Value.Points.Where(pt => LambdaEval.ExecuteBool(pt, _lambda)).ToList();
                if (kept.Count == 0 && !_emitEmpty)
                    return Task.FromResult(NodeResult.OkResult());
                var nb = b.Value.Clone();
                nb.Points = kept;
                return Task.FromResult(NodeResult.Emit(DataItem.From(nb)));
            }
            default:
                return Task.FromResult(NodeResult.OkResult());
        }
    }
}
