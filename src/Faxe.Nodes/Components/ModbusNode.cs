using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_modbus</summary>
[FaxeNode("modbus")]
public sealed class ModbusNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("ip", NodeOptionType.String),
        NodeOption.Define("port", NodeOptionType.Integer, 502L),
        NodeOption.Define("every", NodeOptionType.Duration, null),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("device", NodeOptionType.Integer, 255L),
        NodeOption.Define("function", NodeOptionType.BinaryList),
        NodeOption.Define("from", NodeOptionType.IntegerList),
        NodeOption.Define("count", NodeOptionType.IntegerList),
        NodeOption.Define("as", NodeOptionType.BinaryList),
        NodeOption.Define("output", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("signed", NodeOptionType.Any, null),
        NodeOption.Define("max_connections", NodeOptionType.Any, null),
        NodeOption.Define("round", NodeOptionType.Integer, 0L),
        NodeOption.Define("timeout", NodeOptionType.Duration, "7s"),
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
