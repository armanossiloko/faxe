using System.Threading.Channels;
using Faxe.Core.Data;
using Faxe.Core.Models;

namespace Faxe.Flow;

/// <summary>Runtime host for one compiled DFS graph (mirrors df_graph).</summary>
public sealed class FlowGraph : IAsyncDisposable
{
    private readonly NodeRegistry _registry;
    private readonly Dictionary<string, NodeRuntime> _nodes = new(StringComparer.Ordinal);
    private readonly List<(string Src, int OutPort, string Dest, int InPort)> _edges = new();
    private CancellationTokenSource? _cts;
    private readonly List<Task> _workers = new();

    public FlowGraph(string graphId, GraphDefinition definition, NodeRegistry registry)
    {
        GraphId = graphId;
        Definition = definition;
        _registry = registry;
    }

    public string GraphId { get; }
    public GraphDefinition Definition { get; }
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_cts is not null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        foreach (var n in Definition.Nodes)
        {
            var impl = _registry.Create(n.Type);
            var bound = OptionBinder.Bind(impl, n.Options);
            var runtime = new NodeRuntime(GraphId, n.Name, n.Type, impl);
            _nodes[n.Name] = runtime;
            await impl.InitAsync(n.Name, Array.Empty<int>(), bound, _cts.Token);
            if (impl is FaxeNodeBase bas)
                bas.AttachContext(new FlowNodeContext(GraphId, n.Name, runtime.EmitFromContextAsync));
        }

        foreach (var e in Definition.Edges)
        {
            _edges.Add((e.Source, e.OutPort, e.Dest, e.InPort));
            if (!_nodes.TryGetValue(e.Source, out var src) || !_nodes.TryGetValue(e.Dest, out var dest))
                throw new InvalidOperationException($"Invalid edge {e.Source}->{e.Dest}");
            src.Subscribe(e.OutPort, dest, e.InPort);
        }

        foreach (var runtime in _nodes.Values)
            _workers.Add(runtime.RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { await Task.WhenAll(_workers); } catch { /* ignore cancel */ }
        foreach (var n in _nodes.Values)
            await n.Impl.ShutdownAsync(CancellationToken.None);
        _cts.Dispose();
        _cts = null;
        _workers.Clear();
    }

    public object ToGraphMap()
    {
        return new
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

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class NodeRuntime
    {
        private readonly Channel<(int InPort, DataItem Item)> _inbox =
            Channel.CreateUnbounded<(int, DataItem)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        private readonly List<(NodeRuntime Dest, int InPort)>[] _subscribers = CreateSubs();

        public NodeRuntime(string graphId, string name, string type, IFaxeNode impl)
        {
            GraphId = graphId;
            Name = name;
            Type = type;
            Impl = impl;
        }

        public string GraphId { get; }
        public string Name { get; }
        public string Type { get; }
        public IFaxeNode Impl { get; }

        public void Subscribe(int outPort, NodeRuntime dest, int inPort)
        {
            EnsurePort(outPort);
            _subscribers[outPort].Add((dest, inPort));
        }

        public async Task EmitFromContextAsync(int outPort, DataItem item, CancellationToken ct)
        {
            EnsurePort(outPort);
            foreach (var (dest, inPort) in _subscribers[outPort])
                await dest.EnqueueAsync(inPort, item, ct);
        }

        public ValueTask EnqueueAsync(int inPort, DataItem item, CancellationToken ct) =>
            _inbox.Writer.WriteAsync((inPort, item), ct);

        public async Task RunAsync(CancellationToken ct)
        {
            // Kick source nodes that schedule via OnInfo (value_emitter etc.)
            await Impl.OnInfoAsync(new StartSignal(), ct);

            try
            {
                await foreach (var (inPort, item) in _inbox.Reader.ReadAllAsync(ct))
                {
                    var result = await Impl.ProcessAsync(inPort, item, ct);
                    switch (result)
                    {
                        case NodeResult.EmitItem emit:
                            await EmitFromContextAsync(emit.OutPort, emit.Item, ct);
                            break;
                        case NodeResult.Error err:
                            throw new InvalidOperationException($"Node {Name} error: {err.Reason}");
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // normal stop
            }
        }

        private void EnsurePort(int outPort)
        {
            if (outPort < 0 || outPort >= _subscribers.Length)
                throw new ArgumentOutOfRangeException(nameof(outPort));
        }

        private static List<(NodeRuntime, int)>[] CreateSubs()
        {
            var arr = new List<(NodeRuntime, int)>[16];
            for (var i = 0; i < arr.Length; i++)
                arr[i] = new List<(NodeRuntime, int)>();
            return arr;
        }
    }

    public readonly record struct StartSignal;
}
