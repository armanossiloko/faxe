using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Faxe.Core;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>
/// Owns the Faxe <see cref="ActorSystem"/> and tracks running graphs (graph_sup façade).
/// </summary>
public sealed class FlowRuntime : IAsyncDisposable
{
    private readonly NodeRegistry _registry;
    private readonly ActorSystem _system;
    private readonly IActorRef _supervisor;
    private readonly ConcurrentDictionary<string, FlowGraph> _running = new(StringComparer.Ordinal);
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);

    public FlowRuntime(NodeRegistry registry)
    {
        _registry = registry;
        var config = ConfigurationFactory.ParseString("""
            akka {
              loglevel = WARNING
              actor {
                provider = local
                default-dispatcher {
                  type = Dispatcher
                  executor = "thread-pool-executor"
                  throughput = 10
                }
              }
            }
            """);
        _system = ActorSystem.Create("faxe", config);
        _supervisor = _system.ActorOf(GraphsSupervisor.Props(_registry), "graphs");
    }

    public NodeRegistry Registry => _registry;
    public ActorSystem System => _system;

    public async Task StartAsync(TaskRecord task, CancellationToken ct = default)
    {
        if (task.Definition is null)
            throw new InvalidOperationException("Task has no graph definition");
        if (_running.ContainsKey(task.Name))
            return;

        var reply = await _supervisor.Ask<object>(
            new FlowMessages.StartGraph(task.Name, task.Definition, ActorRefs.Nobody),
            AskTimeout,
            ct).ConfigureAwait(false);

        switch (reply)
        {
            case FlowMessages.GraphStarted started:
                var handle = new FlowGraph(task.Name, task.Definition, started.Graph);
                if (!_running.TryAdd(task.Name, handle))
                {
                    started.Graph.Tell(new FlowMessages.StopFlow());
                    return;
                }
                task.IsRunning = true;
                task.LastStart = FaxeTime.Now();
                break;
            case FlowMessages.GraphFailed failed:
                throw new InvalidOperationException(failed.Error);
            default:
                throw new InvalidOperationException($"Unexpected start reply: {reply?.GetType().Name}");
        }
    }

    public async Task StopAsync(TaskRecord task)
    {
        if (_running.TryRemove(task.Name, out var graph))
        {
            graph.IsRunning = false;
            await _supervisor.Ask<object>(
                new FlowMessages.StopGraph(task.Name, ActorRefs.Nobody),
                AskTimeout).ConfigureAwait(false);
            task.IsRunning = false;
            task.LastStop = FaxeTime.Now();
        }
        else
        {
            task.IsRunning = false;
        }
    }

    public bool IsRunning(string taskName) =>
        _running.TryGetValue(taskName, out var g) && g.IsRunning && !g.Actor.IsNobody();

    public FlowGraph? Get(string taskName) =>
        _running.TryGetValue(taskName, out var g) ? g : null;

    public IReadOnlyCollection<string> RunningNames =>
        _running.Where(kv => kv.Value.IsRunning).Select(kv => kv.Key).ToList();

    public async ValueTask DisposeAsync()
    {
        foreach (var name in _running.Keys.ToList())
        {
            if (_running.TryRemove(name, out _))
                _supervisor.Tell(new FlowMessages.StopGraph(name, ActorRefs.Nobody));
        }
        await CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance)
            .ConfigureAwait(false);
    }
}
