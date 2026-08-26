using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_tcppoll</summary>
[FaxeNode("tcppoll")]
public sealed class TcppollNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.Binary),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("count", NodeOptionType.Integer, 1L),
        NodeOption.Define("prefix", NodeOptionType.String, "val_"),
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
