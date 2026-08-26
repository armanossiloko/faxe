using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_mqtt_publish</summary>
[FaxeNode("mqtt_publish")]
public sealed class MqttPublishNode : FaxeNodeBase
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
        NodeOption.Define("topic_field", NodeOptionType.Binary, null),
        NodeOption.Define("topic_lambda", NodeOptionType.Lambda, null),
        NodeOption.Define("retained", NodeOptionType.IsSet, false),
        NodeOption.Define("ssl", NodeOptionType.IsSet, false),
        NodeOption.Define("safe", NodeOptionType.Boolean, false),
        NodeOption.Define("max_mem_queue_size", NodeOptionType.Integer, 700L),
        NodeOption.Define("use_pool", NodeOptionType.Boolean, false),
        NodeOption.Define("request_ack", NodeOptionType.Boolean, false),
        NodeOption.Define("add_seq_check", NodeOptionType.Boolean, false),
        NodeOption.Define("seq_check_topic_depth", NodeOptionType.Integer, 0L),
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
