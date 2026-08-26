using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_state_change_bulk</summary>
[FaxeNode("state_change_bulk")]
public sealed class StateChangeBulkNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("lambda_pattern", NodeOptionType.String, null),
        NodeOption.Define("state_value", NodeOptionType.Any, null),
        NodeOption.Define("field", NodeOptionType.String),
        NodeOption.Define("exclude_fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("enter_as", NodeOptionType.Binary, "state_entered"),
        NodeOption.Define("leave_as", NodeOptionType.Binary, "state_left"),
        NodeOption.Define("state_id_as", NodeOptionType.Binary, "state_id"),
        NodeOption.Define("enter", NodeOptionType.IsSet, false),
        NodeOption.Define("leave", NodeOptionType.IsSet, false),
        NodeOption.Define("enter_keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("leave_keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("prefix", NodeOptionType.String, ""),
        NodeOption.Define("unit", NodeOptionType.Duration, "1s"),
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
