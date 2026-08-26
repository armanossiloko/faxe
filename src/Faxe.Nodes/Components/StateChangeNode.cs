using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_state_change</summary>
[FaxeNode("state_change")]
public sealed class StateChangeNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("lambda", NodeOptionType.Lambda),
        NodeOption.Define("enter_as", NodeOptionType.String, "state_entered"),
        NodeOption.Define("leave_as", NodeOptionType.String, "state_left"),
        NodeOption.Define("state_id_as", NodeOptionType.String, "state_id"),
        NodeOption.Define("enter", NodeOptionType.IsSet, false),
        NodeOption.Define("leave", NodeOptionType.IsSet, false),
        NodeOption.Define("enter_keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("leave_keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("keep", NodeOptionType.StringList, new List<string> {  }),
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
