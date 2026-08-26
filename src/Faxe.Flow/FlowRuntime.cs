using System.Collections.Concurrent;
using Faxe.Core;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>Tracks running graphs; mirrors graph_sup + faxe start/stop.</summary>
public sealed class FlowRuntime
{
    private readonly NodeRegistry _registry;
    private readonly ConcurrentDictionary<string, FlowGraph> _running = new(StringComparer.Ordinal);

    public FlowRuntime(NodeRegistry registry) => _registry = registry;

    public NodeRegistry Registry => _registry;

    public async Task StartAsync(TaskRecord task, CancellationToken ct = default)
    {
        if (task.Definition is null)
            throw new InvalidOperationException("Task has no graph definition");
        if (_running.ContainsKey(task.Name))
            return;

        var graph = new FlowGraph(task.Name, task.Definition, _registry);
        if (!_running.TryAdd(task.Name, graph))
        {
            await graph.DisposeAsync();
            return;
        }

        try
        {
            await graph.StartAsync(ct);
            task.IsRunning = true;
            task.LastStart = FaxeTime.Now();
        }
        catch
        {
            _running.TryRemove(task.Name, out _);
            await graph.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync(TaskRecord task)
    {
        if (_running.TryRemove(task.Name, out var graph))
        {
            await graph.StopAsync();
            task.IsRunning = false;
            task.LastStop = FaxeTime.Now();
        }
        else
        {
            task.IsRunning = false;
        }
    }

    public bool IsRunning(string taskName) => _running.ContainsKey(taskName);

    public FlowGraph? Get(string taskName) =>
        _running.TryGetValue(taskName, out var g) ? g : null;

    public IReadOnlyCollection<string> RunningNames => _running.Keys.ToList();
}
