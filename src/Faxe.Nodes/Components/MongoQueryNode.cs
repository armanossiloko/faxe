using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_mongo_query</summary>
[FaxeNode("mongo_query")]
public sealed class MongoQueryNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer, 27017L),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("database", NodeOptionType.String),
        NodeOption.Define("collection", NodeOptionType.String),
        NodeOption.Define("query", NodeOptionType.String, null),
        NodeOption.Define("as", NodeOptionType.Binary, null),
        NodeOption.Define("time_field", NodeOptionType.String, "ts"),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
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
