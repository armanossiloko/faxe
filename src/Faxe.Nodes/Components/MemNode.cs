using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_mem</summary>
[FaxeNode("mem")]
public sealed class MemNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("field", NodeOptionType.String, null),
        NodeOption.Define("type", NodeOptionType.String, "single"),
        NodeOption.Define("key", NodeOptionType.String, "StreamLookup"),
        NodeOption.Define("default", NodeOptionType.Any, null),
        NodeOption.Define("default_json", NodeOptionType.IsSet, false),
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
