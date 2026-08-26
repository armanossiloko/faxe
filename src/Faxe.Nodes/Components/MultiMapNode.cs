using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_multi_map</summary>
[FaxeNode("multi_map")]
public sealed class MultiMapNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList),
        NodeOption.Define("match_fields", NodeOptionType.StringList),
        NodeOption.Define("as", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("lookup", NodeOptionType.Any),
        NodeOption.Define("select_fields", NodeOptionType.StringList),
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
