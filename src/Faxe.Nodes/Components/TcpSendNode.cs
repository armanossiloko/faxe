using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_tcp_send</summary>
[FaxeNode("tcp_send")]
public sealed class TcpSendNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.Binary),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("packet", NodeOptionType.Any, 2L),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("response_as", NodeOptionType.String, null),
        NodeOption.Define("response_json", NodeOptionType.IsSet, false),
        NodeOption.Define("response_timeout", NodeOptionType.Duration, "5s"),
        NodeOption.Define("msg_text", NodeOptionType.String, null),
        NodeOption.Define("msg_json", NodeOptionType.String, null),
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
