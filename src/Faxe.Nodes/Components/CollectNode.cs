using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_collect</summary>
[FaxeNode("collect")]
public sealed class CollectNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("key_fields", NodeOptionType.StringList),
        NodeOption.Define("add", NodeOptionType.Lambda, null),
        NodeOption.Define("remove", NodeOptionType.Lambda, null),
        NodeOption.Define("update", NodeOptionType.Any, null),
        NodeOption.Define("update_mode", NodeOptionType.String, null),
        NodeOption.Define("emit_every", NodeOptionType.Duration, null),
        NodeOption.Define("emit_unchanged", NodeOptionType.Boolean, true),
        NodeOption.Define("tag_added", NodeOptionType.Boolean, false),
        NodeOption.Define("tag_updated", NodeOptionType.Boolean, false),
        NodeOption.Define("tag_removed", NodeOptionType.Boolean, false),
        NodeOption.Define("include_removed", NodeOptionType.Boolean, false),
        NodeOption.Define("keep", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("keep_as", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("as", NodeOptionType.String, "collected"),
        NodeOption.Define("max_age", NodeOptionType.Duration, "3h"),
        NodeOption.Define("max_ts_age", NodeOptionType.Duration, null),
        NodeOption.Define("tag_value", NodeOptionType.Any, 1L),
        NodeOption.Define("merge", NodeOptionType.Boolean, false),
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
