using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_postgre_statement</summary>
[FaxeNode("postgre_statement")]
public sealed class PostgreStatementNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("tls", NodeOptionType.Boolean, false),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("statement", NodeOptionType.String, null),
        NodeOption.Define("statement_field", NodeOptionType.String, null),
        NodeOption.Define("retries", NodeOptionType.Integer, 2L),
        NodeOption.Define("start_on_trigger", NodeOptionType.Boolean, false),
        NodeOption.Define("every", NodeOptionType.Duration, null),
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
