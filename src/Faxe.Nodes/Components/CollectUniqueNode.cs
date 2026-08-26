using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_collect_unique</summary>
[FaxeNode("collect_unique")]
public sealed class CollectUniqueNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("field", NodeOptionType.String),
        NodeOption.Define("min_vals", NodeOptionType.Integer, 1L),
        NodeOption.Define("keep", NodeOptionType.StringList),
        NodeOption.Define("keep_as", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("as", NodeOptionType.String, null),
        NodeOption.Define("max_age", NodeOptionType.Duration, null),
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
