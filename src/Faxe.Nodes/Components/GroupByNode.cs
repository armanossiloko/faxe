using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_group_by</summary>
[FaxeNode("group_by")]
public sealed class GroupByNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("lambda", NodeOptionType.Lambda, null),
        NodeOption.Define("reset_timeout", NodeOptionType.Duration, "2m"),
        NodeOption.Define("debatch", NodeOptionType.Boolean, false),
        NodeOption.Define("emit_empty", NodeOptionType.Boolean, false),
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
