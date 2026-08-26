using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_triggered_timeout</summary>
[FaxeNode("triggered_timeout")]
public sealed class TriggeredTimeoutNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("timeout", NodeOptionType.Duration),
        NodeOption.Define("fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("field_values", NodeOptionType.List, new List<string> {  }),
        NodeOption.Define("cancel_fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("cancel_field_values", NodeOptionType.List, new List<string> {  }),
        NodeOption.Define("timeout_trigger_port", NodeOptionType.Integer, 1L),
        NodeOption.Define("timeout_trigger", NodeOptionType.Lambda, null),
        NodeOption.Define("cancel_trigger", NodeOptionType.Lambda, null),
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
