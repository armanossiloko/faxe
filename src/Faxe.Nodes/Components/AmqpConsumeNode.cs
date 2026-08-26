using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_amqp_consume</summary>
[FaxeNode("amqp_consume")]
public sealed class AmqpConsumeNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.Binary, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("ssl", NodeOptionType.IsSet, false),
        NodeOption.Define("vhost", NodeOptionType.String, "/"),
        NodeOption.Define("vhost_prefix", NodeOptionType.String, null),
        NodeOption.Define("routing_key", NodeOptionType.String, null),
        NodeOption.Define("bindings", NodeOptionType.StringList, new List<string> {  }),
        NodeOption.Define("qx_name", NodeOptionType.String, null),
        NodeOption.Define("queue", NodeOptionType.Any, null),
        NodeOption.Define("queue_type", NodeOptionType.String, null),
        NodeOption.Define("takeover_host", NodeOptionType.Binary, null),
        NodeOption.Define("takeover_port", NodeOptionType.Integer, 0L),
        NodeOption.Define("takeover_user", NodeOptionType.String, null),
        NodeOption.Define("takeover_pass", NodeOptionType.String, null),
        NodeOption.Define("takeover_ssl", NodeOptionType.IsSet, false),
        NodeOption.Define("takeover", NodeOptionType.Boolean, false),
        NodeOption.Define("takeover_timeout", NodeOptionType.Duration, "5m"),
        NodeOption.Define("takeover_queue", NodeOptionType.String, null),
        NodeOption.Define("takeover_queue_prefix", NodeOptionType.String, null),
        NodeOption.Define("takeover_queue_type", NodeOptionType.String, null),
        NodeOption.Define("takeover_queue_vhost", NodeOptionType.String, null),
        NodeOption.Define("queue_prefix", NodeOptionType.String, null),
        NodeOption.Define("consumer_tag", NodeOptionType.String, null),
        NodeOption.Define("exchange", NodeOptionType.String, null),
        NodeOption.Define("root_exchange", NodeOptionType.String, null),
        NodeOption.Define("exchange_prefix", NodeOptionType.String, null),
        NodeOption.Define("prefetch", NodeOptionType.Integer, 120L),
        NodeOption.Define("ack_every", NodeOptionType.Integer, 50L),
        NodeOption.Define("ack_after", NodeOptionType.Duration, "5s"),
        NodeOption.Define("use_flow_ack", NodeOptionType.Any, null),
        NodeOption.Define("safe", NodeOptionType.Boolean, false),
        NodeOption.Define("dt_field", NodeOptionType.String, "ts"),
        NodeOption.Define("dt_format", NodeOptionType.String, null),
        NodeOption.Define("clean_field_names", NodeOptionType.Boolean, false),
        NodeOption.Define("include_topic", NodeOptionType.Any, null),
        NodeOption.Define("topic_as", NodeOptionType.String, "topic"),
        NodeOption.Define("as", NodeOptionType.String, null),
        NodeOption.Define("confirm", NodeOptionType.Boolean, true),
        NodeOption.Define("dedup_size", NodeOptionType.Integer, 400L),
        NodeOption.Define("passive", NodeOptionType.Any, null),
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
