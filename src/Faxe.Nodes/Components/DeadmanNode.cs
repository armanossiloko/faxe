using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_deadman</summary>
[FaxeNode("deadman")]
public sealed class DeadmanNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("timeout", NodeOptionType.Duration),
        NodeOption.Define("fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("field_values", NodeOptionType.List, new List<string> {  }),
        NodeOption.Define("silent_time", NodeOptionType.Duration, "0ms"),
        NodeOption.Define("repeat_last", NodeOptionType.IsSet, false),
        NodeOption.Define("repeat_with_new_ts", NodeOptionType.Any, null),
        NodeOption.Define("repeat_interval", NodeOptionType.Duration, null),
        NodeOption.Define("trigger_on_value", NodeOptionType.IsSet, false),
        NodeOption.Define("no_forward", NodeOptionType.IsSet, false),
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
