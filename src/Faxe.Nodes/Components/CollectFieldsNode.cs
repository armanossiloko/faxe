using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_collect_fields</summary>
[FaxeNode("collect_fields")]
public sealed class CollectFieldsNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList),
        NodeOption.Define("default", NodeOptionType.Any, null),
        NodeOption.Define("emit_unchanged", NodeOptionType.Boolean, true),
        NodeOption.Define("keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("keep_as", NodeOptionType.StringList, new List<string> {  }),
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
