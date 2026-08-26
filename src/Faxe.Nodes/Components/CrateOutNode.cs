using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_crate_out</summary>
[FaxeNode("crate_out")]
public sealed class CrateOutNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("tls", NodeOptionType.IsSet, false),
        NodeOption.Define("table", NodeOptionType.Any),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("database", NodeOptionType.String, "doc"),
        NodeOption.Define("db_fields", NodeOptionType.List, new List<string> {  }),
        NodeOption.Define("faxe_fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("remaining_fields_as", NodeOptionType.String, null),
        NodeOption.Define("max_retries", NodeOptionType.Integer, 0L),
        NodeOption.Define("error_trace", NodeOptionType.Boolean, false),
        NodeOption.Define("ignore_response_timeout", NodeOptionType.Boolean, false),
        NodeOption.Define("use_flow_ack", NodeOptionType.Boolean, false),
        NodeOption.Define("deduplicate", NodeOptionType.Boolean, true),
        NodeOption.Define("pg_port", NodeOptionType.Integer, 0L),
        NodeOption.Define("pg_tls", NodeOptionType.Boolean, false),
        NodeOption.Define("pg_user", NodeOptionType.String, null),
        NodeOption.Define("pg_pass", NodeOptionType.String, null),
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
