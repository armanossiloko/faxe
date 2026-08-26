using Akka.Actor;

namespace Faxe.Flow;

/// <summary>Top-level supervisor for all running graphs — mirrors graph_sup.</summary>
public sealed class GraphsSupervisor : ReceiveActor
{
    private readonly NodeRegistry _registry;
    private readonly Dictionary<string, IActorRef> _graphs = new(StringComparer.Ordinal);

    public GraphsSupervisor(NodeRegistry registry)
    {
        _registry = registry;

        Receive<FlowMessages.StartGraph>(Start);
        Receive<FlowMessages.StopGraph>(Stop);
        Receive<Terminated>(OnTerminated);
    }

    public static Props Props(NodeRegistry registry) =>
        Akka.Actor.Props.Create(() => new GraphsSupervisor(registry));

    protected override SupervisorStrategy SupervisorStrategy() =>
        new OneForOneStrategy(
            maxNrOfRetries: -1,
            withinTimeRange: TimeSpan.FromSeconds(1),
            localOnlyDecider: _ => Directive.Stop);

    private void Start(FlowMessages.StartGraph msg)
    {
        var replyTo = msg.ReplyTo.IsNobody() ? Sender : msg.ReplyTo;
        if (_graphs.TryGetValue(msg.GraphId, out var existing))
        {
            replyTo.Tell(new FlowMessages.GraphStarted(msg.GraphId, existing));
            return;
        }

        try
        {
            var graph = Context.ActorOf(
                GraphActor.Props(msg.GraphId, msg.Definition, _registry),
                Sanitize(msg.GraphId));
            Context.Watch(graph);
            _graphs[msg.GraphId] = graph;
            replyTo.Tell(new FlowMessages.GraphStarted(msg.GraphId, graph));
        }
        catch (Exception ex)
        {
            replyTo.Tell(new FlowMessages.GraphFailed(msg.GraphId, ex.Message));
        }
    }

    private void Stop(FlowMessages.StopGraph msg)
    {
        var replyTo = msg.ReplyTo.IsNobody() ? Sender : msg.ReplyTo;
        if (_graphs.TryGetValue(msg.GraphId, out var graph))
            graph.Tell(new FlowMessages.StopFlow());
        replyTo.Tell(new FlowMessages.GraphStopped(msg.GraphId));
    }

    private void OnTerminated(Terminated t)
    {
        var key = _graphs.FirstOrDefault(kv => kv.Value.Equals(t.ActorRef)).Key;
        if (key is not null)
            _graphs.Remove(key);
    }

    private static string Sanitize(string name) =>
        Uri.EscapeDataString(name).Replace('%', '_');
}
