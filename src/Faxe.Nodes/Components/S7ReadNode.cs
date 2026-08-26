using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_s7read</summary>
[FaxeNode("s7read")]
public sealed class S7ReadNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.Binary),
        NodeOption.Define("port", NodeOptionType.Integer, 102L),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("slot", NodeOptionType.Integer, 1L),
        NodeOption.Define("rack", NodeOptionType.Integer, 0L),
        NodeOption.Define("vars", NodeOptionType.StringList),
        NodeOption.Define("vars_prefix", NodeOptionType.String, null),
        NodeOption.Define("as", NodeOptionType.BinaryList, new List<string> {  }),
        NodeOption.Define("as_prefix", NodeOptionType.String, null),
        NodeOption.Define("diff", NodeOptionType.IsSet, false),
        NodeOption.Define("merge_field", NodeOptionType.String, null),
        NodeOption.Define("byte_offset", NodeOptionType.Integer, 0L),
        NodeOption.Define("use_pool", NodeOptionType.Any, null),
        NodeOption.Define("standalone", NodeOptionType.Any, null),
        NodeOption.Define("optimized", NodeOptionType.Any, null),
        NodeOption.Define("native", NodeOptionType.Any, null),
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
