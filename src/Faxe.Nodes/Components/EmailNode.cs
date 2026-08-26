using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_email</summary>
[FaxeNode("email")]
public sealed class EmailNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("from_address", NodeOptionType.Binary, null),
        NodeOption.Define("smtp_relay", NodeOptionType.Binary, null),
        NodeOption.Define("smtp_user", NodeOptionType.Any, null),
        NodeOption.Define("smtp_pass", NodeOptionType.Any, null),
        NodeOption.Define("smtp_port", NodeOptionType.Integer, 0L),
        NodeOption.Define("smtp_tls", NodeOptionType.IsSet, false),
        NodeOption.Define("template", NodeOptionType.Binary, null),
        NodeOption.Define("subject", NodeOptionType.Any, null),
        NodeOption.Define("body", NodeOptionType.Any, null),
        NodeOption.Define("body_field", NodeOptionType.String, null),
        NodeOption.Define("subject_field", NodeOptionType.String, null),
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
