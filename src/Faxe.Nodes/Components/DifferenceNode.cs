using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Alias of stats_difference</summary>
[FaxeNode("difference")]
public sealed class DifferenceNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => Array.Empty<NodeOption>();

    private IReadOnlyDictionary<string, object?> _opts = new Dictionary<string, object?>();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _opts = options;
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
        => Task.FromResult(NodeResult.Emit(item));
}
