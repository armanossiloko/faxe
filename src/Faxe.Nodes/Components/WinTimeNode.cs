using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_win_time</summary>
[FaxeNode("win_time")]
public sealed class WinTimeNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("period", NodeOptionType.Duration, null),
        NodeOption.Define("every", NodeOptionType.Duration),
        NodeOption.Define("fill_period", NodeOptionType.IsSet, false),
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
