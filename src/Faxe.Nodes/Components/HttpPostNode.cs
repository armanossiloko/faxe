using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_http_post</summary>
[FaxeNode("http_post")]
public sealed class HttpPostNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer),
        NodeOption.Define("path", NodeOptionType.String, "/"),
        NodeOption.Define("tls", NodeOptionType.Boolean, false),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("header_names", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("header_values", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("field", NodeOptionType.String, null),
        NodeOption.Define("without", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("response_as", NodeOptionType.String, "data"),
        NodeOption.Define("method", NodeOptionType.String, "post"),
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
