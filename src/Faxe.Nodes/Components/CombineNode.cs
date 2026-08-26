using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_combine</summary>
[FaxeNode("combine")]
public sealed class CombineNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("tags", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("aliases", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("prefix", NodeOptionType.Binary, null),
        NodeOption.Define("prefix_delimiter", NodeOptionType.Binary, null),
        NodeOption.Define("merge_field", NodeOptionType.Binary, null),
        NodeOption.Define("nofill", NodeOptionType.IsSet, false),
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
