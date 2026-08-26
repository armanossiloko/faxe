using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_amqp_publish</summary>
[FaxeNode("amqp_publish")]
public sealed class AmqpPublishNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("host", NodeOptionType.String, null),
        NodeOption.Define("port", NodeOptionType.Integer, 0L),
        NodeOption.Define("user", NodeOptionType.String, null),
        NodeOption.Define("pass", NodeOptionType.String, null),
        NodeOption.Define("vhost", NodeOptionType.String, null),
        NodeOption.Define("vhost_prefix", NodeOptionType.String, null),
        NodeOption.Define("routing_key", NodeOptionType.String, null),
        NodeOption.Define("routing_key_lambda", NodeOptionType.Lambda, null),
        NodeOption.Define("routing_key_field", NodeOptionType.String, null),
        NodeOption.Define("exchange", NodeOptionType.String, null),
        NodeOption.Define("ssl", NodeOptionType.IsSet, false),
        NodeOption.Define("persistent", NodeOptionType.Any, null),
        NodeOption.Define("qos", NodeOptionType.Integer, 0L),
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
