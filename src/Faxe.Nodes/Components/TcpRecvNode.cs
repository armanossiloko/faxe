using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_tcp_recv</summary>
[FaxeNode("tcp_recv")]
public sealed class TcpRecvNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.Binary, null),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("as", NodeOptionType.Binary, "data"),
        NodeOption.Define("as", NodeOptionType.Binary, null),
        NodeOption.Define("extract", NodeOptionType.IsSet, false),
        NodeOption.Define("parser", NodeOptionType.String, null),
        NodeOption.Define("changed", NodeOptionType.IsSet, false),
        NodeOption.Define("packet", NodeOptionType.Any, 2L),
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
