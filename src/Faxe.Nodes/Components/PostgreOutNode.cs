using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_postgre_out</summary>
[FaxeNode("postgre_out")]
public sealed class PostgreOutNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("user", NodeOptionType.String),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("database", NodeOptionType.String),
        NodeOption.Define("table", NodeOptionType.String),
        NodeOption.Define("db_fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("faxe_fields", NodeOptionType.StringList, new List<string> {  }),
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
