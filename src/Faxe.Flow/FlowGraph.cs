using Akka.Actor;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>Handle for a running Akka-backed graph (API/graph introspection).</summary>
public sealed class FlowGraph
{
    public FlowGraph(string graphId, GraphDefinition definition, IActorRef graphActor)
    {
        GraphId = graphId;
        Definition = definition;
        Actor = graphActor;
    }

    public string GraphId { get; }
    public GraphDefinition Definition { get; }
    public IActorRef Actor { get; }
    public bool IsRunning { get; set; } = true;

    public object ToGraphMap() => new
    {
        nodes = Definition.Nodes.Select(n => new { name = n.Name, type = n.Type, options = n.Options }),
        edges = Definition.Edges.Select(e => new
        {
            source = e.Source,
            out_port = e.OutPort,
            dest = e.Dest,
            in_port = e.InPort
        })
    };
}
