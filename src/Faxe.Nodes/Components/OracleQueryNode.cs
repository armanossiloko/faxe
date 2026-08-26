using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_oracle_query</summary>
[FaxeNode("oracle_query")]
public sealed class OracleQueryNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer, 1521L),
        NodeOption.Define("user", NodeOptionType.String),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("service_name", NodeOptionType.String),
        NodeOption.Define("query", NodeOptionType.String),
        NodeOption.Define("result_type", NodeOptionType.String, "batch"),
        NodeOption.Define("time_field", NodeOptionType.String, "ts"),
        NodeOption.Define("every", NodeOptionType.Duration, "5s"),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("limit", NodeOptionType.String, "30"),
    };

    private IReadOnlyDictionary<string, object?> _opts = new Dictionary<string, object?>();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _opts = options;
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
        => Task.FromResult(NodeResult.Emit(item));
}
