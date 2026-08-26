using Akka.Actor;
using Faxe.Core.Data;

namespace Faxe.Flow;

/// <summary>
/// One actor per DFS node — mirrors <c>df_component</c> gen_server.
/// Mailbox serializes Process/Emit; failures are supervised by GraphActor.
/// </summary>
public sealed class NodeActor : ReceiveActor
{
    private readonly string _graphId;
    private readonly string _nodeId;
    private readonly string _nodeType;
    private readonly IFaxeNode _impl;
    private readonly IReadOnlyDictionary<string, object?> _options;
    private readonly Dictionary<int, List<(IActorRef Dest, int InPort)>> _subscribers = new();

    public NodeActor(
        string graphId,
        string nodeId,
        string nodeType,
        IFaxeNode impl,
        IReadOnlyDictionary<string, object?> options)
    {
        _graphId = graphId;
        _nodeId = nodeId;
        _nodeType = nodeType;
        _impl = impl;
        _options = options;

        Receive<FlowMessages.WireSubscription>(Wire);
        ReceiveAsync<FlowMessages.StartFlow>(OnStartAsync);
        ReceiveAsync<FlowMessages.DataItemMsg>(OnDataAsync);
    }

    public static Props Props(
        string graphId,
        string nodeId,
        string nodeType,
        IFaxeNode impl,
        IReadOnlyDictionary<string, object?> options) =>
        Akka.Actor.Props.Create(() => new NodeActor(graphId, nodeId, nodeType, impl, options));

    protected override void PreStart()
    {
        _impl.InitAsync(_nodeId, Array.Empty<int>(), _options, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();

        if (_impl is FaxeNodeBase bas)
            bas.AttachContext(new FlowNodeContext(_graphId, _nodeId, EmitFromContextAsync));
    }

    protected override void PostStop()
    {
        try
        {
            _impl.ShutdownAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            // ignore shutdown errors
        }
    }

    private void Wire(FlowMessages.WireSubscription msg)
    {
        if (!_subscribers.TryGetValue(msg.OutPort, out var list))
        {
            list = new List<(IActorRef, int)>();
            _subscribers[msg.OutPort] = list;
        }
        list.Add((msg.Dest, msg.InPort));
    }

    private async Task OnStartAsync(FlowMessages.StartFlow _)
    {
        await _impl.OnInfoAsync(new FlowMessages.StartSignal(), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task OnDataAsync(FlowMessages.DataItemMsg msg)
    {
        var result = await _impl.ProcessAsync(msg.InPort, msg.Item, CancellationToken.None).ConfigureAwait(false);
        switch (result)
        {
            case NodeResult.EmitItem emit:
                Publish(emit.OutPort, emit.Item);
                break;
            case NodeResult.Error err:
                throw new InvalidOperationException($"Node '{_nodeId}' ({_nodeType}): {err.Reason}");
        }
    }

    private Task EmitFromContextAsync(int outPort, DataItem item, CancellationToken cancellationToken)
    {
        Publish(outPort, item);
        return Task.CompletedTask;
    }

    private void Publish(int outPort, DataItem item)
    {
        if (!_subscribers.TryGetValue(outPort, out var list))
            return;
        foreach (var (dest, inPort) in list)
            dest.Tell(new FlowMessages.DataItemMsg(inPort, item));
    }
}
