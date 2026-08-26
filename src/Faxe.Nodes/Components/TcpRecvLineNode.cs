using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_tcp_recv_line</summary>
[FaxeNode("tcp_recv_line")]
public sealed class TcpRecvLineNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.Binary),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("as", NodeOptionType.Binary, "data"),
        NodeOption.Define("extract", NodeOptionType.IsSet, false),
        NodeOption.Define("line_delimiter", NodeOptionType.Binary, null),
        NodeOption.Define("parser", NodeOptionType.String, null),
        NodeOption.Define("min_length", NodeOptionType.Integer, 61L),
        NodeOption.Define("changed", NodeOptionType.IsSet, false),
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
