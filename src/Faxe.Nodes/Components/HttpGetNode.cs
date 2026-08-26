using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_http_get</summary>
[FaxeNode("http_get")]
public sealed class HttpGetNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer, 80L),
        NodeOption.Define("path", NodeOptionType.String, "/"),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, ""),
        NodeOption.Define("payload_type", NodeOptionType.String, null),
        NodeOption.Define("param_keys", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("param_values", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("tls", NodeOptionType.IsSet, false),
        NodeOption.Define("retries", NodeOptionType.Any, null),
        NodeOption.Define("as", NodeOptionType.String, null),
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
