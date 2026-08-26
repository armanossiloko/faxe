using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_fields_to_array</summary>
[FaxeNode("fields_to_array")]
public sealed class FieldsToArrayNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList),
        NodeOption.Define("key_name", NodeOptionType.String),
        NodeOption.Define("value_name", NodeOptionType.String),
        NodeOption.Define("ts_as", NodeOptionType.String, null),
        NodeOption.Define("keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("as", NodeOptionType.String),
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
