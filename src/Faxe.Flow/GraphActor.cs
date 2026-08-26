using Akka.Actor;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>
/// One actor per running task graph — mirrors <c>df_graph</c>.
/// Children are NodeActors with OneForOne restart supervision.
/// </summary>
public sealed class GraphActor : ReceiveActor
{
    private readonly string _graphId;
    private readonly GraphDefinition _definition;
    private readonly NodeRegistry _registry;
    private readonly Dictionary<string, IActorRef> _nodes = new(StringComparer.Ordinal);

    public GraphActor(string graphId, GraphDefinition definition, NodeRegistry registry)
    {
        _graphId = graphId;
        _definition = definition;
        _registry = registry;

        Receive<FlowMessages.StopFlow>(_ => Context.Stop(Self));
    }

    public static Props Props(string graphId, GraphDefinition definition, NodeRegistry registry) =>
        Akka.Actor.Props.Create(() => new GraphActor(graphId, definition, registry));

    protected override SupervisorStrategy SupervisorStrategy() =>
        new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                Console.Error.WriteLine($"[faxe] node failure in graph {_graphId}: {ex.Message}");
                return Directive.Restart;
            });

    protected override void PreStart()
    {
        foreach (var n in _definition.Nodes)
        {
            var impl = _registry.Create(n.Type);
            var bound = OptionBinder.Bind(impl, n.Options);
            // Actor names must be URL-safe; DFS ids are alphanumeric
            var child = Context.ActorOf(
                NodeActor.Props(_graphId, n.Name, n.Type, impl, bound),
                SanitizeName(n.Name));
            _nodes[n.Name] = child;
        }

        foreach (var e in _definition.Edges)
        {
            if (!_nodes.TryGetValue(e.Source, out var src) || !_nodes.TryGetValue(e.Dest, out var dest))
                throw new InvalidOperationException($"Invalid edge {e.Source}->{e.Dest} in graph {_graphId}");
            src.Tell(new FlowMessages.WireSubscription(e.OutPort, dest, e.InPort));
        }

        foreach (var child in _nodes.Values)
            child.Tell(new FlowMessages.StartFlow());
    }

    private static string SanitizeName(string name)
    {
        // Akka actor names: no spaces; DFS ids are already like value_emitter1
        return name.Replace('/', '_').Replace('\\', '_');
    }
}
