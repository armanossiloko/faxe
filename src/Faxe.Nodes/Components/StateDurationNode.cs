using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_state_duration</summary>
[FaxeNode("state_duration")]
public sealed class StateDurationNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("lambda", NodeOptionType.Lambda),
        NodeOption.Define("as", NodeOptionType.Binary, "state_duration"),
        NodeOption.Define("unit", NodeOptionType.Duration, "1s"),
        NodeOption.Define("emit_total", NodeOptionType.IsSet, false),
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
