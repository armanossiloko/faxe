using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_join2</summary>
[FaxeNode("join2")]
public sealed class Join2Node : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("joined", NodeOptionType.Any, null),
        NodeOption.Define("prefix", NodeOptionType.StringList, new List<string> { "", "" }),
        NodeOption.Define("merge_field", NodeOptionType.String, null),
        NodeOption.Define("missing_timeout", NodeOptionType.Duration, "20s"),
        NodeOption.Define("tolerance", NodeOptionType.Duration, "2s"),
        NodeOption.Define("full", NodeOptionType.Boolean, true),
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
