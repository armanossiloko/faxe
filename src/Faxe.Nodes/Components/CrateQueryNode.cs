using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_crate_query</summary>
[FaxeNode("crate_query")]
public sealed class CrateQueryNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("tls", NodeOptionType.Boolean, false),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("database", NodeOptionType.String, null),
        NodeOption.Define("query", NodeOptionType.String),
        NodeOption.Define("time_field", NodeOptionType.String, "ts"),
        NodeOption.Define("every", NodeOptionType.Duration, "5s"),
        NodeOption.Define("period", NodeOptionType.Duration, "1h"),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("group_by_time", NodeOptionType.Duration, "2m"),
        NodeOption.Define("group_by", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("limit", NodeOptionType.String, "30"),
        NodeOption.Define("result_type", NodeOptionType.String, "batch"),
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
