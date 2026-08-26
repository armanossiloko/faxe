using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_http_listen</summary>
[FaxeNode("http_listen")]
public sealed class HttpListenNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("port", NodeOptionType.Integer, 8899L),
        NodeOption.Define("path", NodeOptionType.String, "/"),
        NodeOption.Define("tls", NodeOptionType.IsSet, false),
        NodeOption.Define("payload_type", NodeOptionType.String, null),
        NodeOption.Define("content_type", NodeOptionType.String, null),
        NodeOption.Define("as", NodeOptionType.String, null),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
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
