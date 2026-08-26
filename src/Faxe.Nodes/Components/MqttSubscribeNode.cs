using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_mqtt_subscribe</summary>
[FaxeNode("mqtt_subscribe")]
public sealed class MqttSubscribeNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.Binary, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("client_id", NodeOptionType.String, null),
        NodeOption.Define("qos", NodeOptionType.Integer, 1L),
        NodeOption.Define("version", NodeOptionType.String, null),
        NodeOption.Define("topic", NodeOptionType.Binary, null),
        NodeOption.Define("topics", NodeOptionType.BinaryList, new List<string> {  }),
        NodeOption.Define("dt_field", NodeOptionType.String, "ts"),
        NodeOption.Define("dt_format", NodeOptionType.String, null),
        NodeOption.Define("include_topic", NodeOptionType.Any, null),
        NodeOption.Define("topic_as", NodeOptionType.String, "topic"),
        NodeOption.Define("as", NodeOptionType.String, null),
        NodeOption.Define("ssl", NodeOptionType.IsSet, false),
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
