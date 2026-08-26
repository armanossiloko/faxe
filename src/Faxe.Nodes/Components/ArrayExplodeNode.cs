using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_array_explode</summary>
[FaxeNode("array_explode")]
public sealed class ArrayExplodeNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList),
        NodeOption.Define("as", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("time_offset", NodeOptionType.Duration, "1s"),
        NodeOption.Define("keep", NodeOptionType.StringList, new List<string> {  }),
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
