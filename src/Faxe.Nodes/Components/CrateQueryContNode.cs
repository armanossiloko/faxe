using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_crate_query_cont</summary>
[FaxeNode("crate_query_cont")]
public sealed class CrateQueryContNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("ssl", NodeOptionType.Boolean, false),
        NodeOption.Define("tls", NodeOptionType.Boolean, false),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("database", NodeOptionType.String, null),
        NodeOption.Define("query", NodeOptionType.Any),
        NodeOption.Define("setup_query", NodeOptionType.Any, null),
        NodeOption.Define("setup_vars", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("setup_ts", NodeOptionType.Any, null),
        NodeOption.Define("filter_time_field", NodeOptionType.String, "ts"),
        NodeOption.Define("result_time_field", NodeOptionType.String, null),
        NodeOption.Define("offset", NodeOptionType.Duration, "20s"),
        NodeOption.Define("period", NodeOptionType.Duration, "1h"),
        NodeOption.Define("min_interval", NodeOptionType.Duration, "5s"),
        NodeOption.Define("query_timeout", NodeOptionType.Duration, "15s"),
        NodeOption.Define("start", NodeOptionType.String),
        NodeOption.Define("start_delay", NodeOptionType.Duration, null),
        NodeOption.Define("stop", NodeOptionType.String, null),
        NodeOption.Define("stop_flow", NodeOptionType.Boolean, true),
        NodeOption.Define("result_type", NodeOptionType.String, "batch"),
        NodeOption.Define("extended_log", NodeOptionType.Boolean, false),
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
