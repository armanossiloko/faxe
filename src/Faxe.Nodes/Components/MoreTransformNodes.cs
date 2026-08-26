using Faxe.Core;
using Faxe.Core.Data;
using Faxe.Dfs;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

[FaxeNode("set")]
public sealed class SetNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.BinaryList, new List<string>()),
        NodeOption.Define("field_values", NodeOptionType.List, new List<object>()),
        NodeOption.Define("tags", NodeOptionType.BinaryList, new List<string>()),
        NodeOption.Define("tag_values", NodeOptionType.List, new List<object>())
    };

    private List<string> _fields = new();
    private List<object?> _values = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _fields = Opt(options, "fields", new List<string>());
        _values = options.TryGetValue("field_values", out var fv) && fv is IEnumerable<object?> en
            ? en.Cast<object?>().ToList() : new();
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        void Apply(DataPoint p)
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                var val = i < _values.Count ? _values[i] : null;
                if (val is LambdaExpression le) val = LambdaEval.Execute(p, le.Body);
                FlowData.Set(p, _fields[i], val);
            }
        }
        if (item is DataItem.Point p) { Apply(p.Value); return Task.FromResult(NodeResult.Emit(item)); }
        if (item is DataItem.Batch b) { foreach (var pt in b.Value.Points) Apply(pt); return Task.FromResult(NodeResult.Emit(item)); }
        return Task.FromResult(NodeResult.OkResult());
    }
}

[FaxeNode("rename")]
public sealed class RenameNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.BinaryList),
        NodeOption.Define("as", NodeOptionType.BinaryList)
    };

    private List<string> _fields = new();
    private List<string> _as = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _fields = Opt(options, "fields", new List<string>());
        _as = Opt(options, "as", new List<string>());
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        void Apply(DataPoint p)
        {
            for (var i = 0; i < Math.Min(_fields.Count, _as.Count); i++)
                FlowData.Rename(p, _fields[i], _as[i]);
        }
        if (item is DataItem.Point p) { Apply(p.Value); return Task.FromResult(NodeResult.Emit(item)); }
        if (item is DataItem.Batch b) { foreach (var pt in b.Value.Points) Apply(pt); return Task.FromResult(NodeResult.Emit(item)); }
        return Task.FromResult(NodeResult.OkResult());
    }
}

[FaxeNode("eval")]
public sealed class EvalNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("lambdas", NodeOptionType.List),
        NodeOption.Define("as", NodeOptionType.BinaryList)
    };

    private List<string> _lambdas = new();
    private List<string> _as = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        if (options.TryGetValue("lambdas", out var l) && l is IEnumerable<object?> en)
        {
            _lambdas = en.Select(x => x switch
            {
                LambdaExpression le => le.Body,
                _ => Convert.ToString(x) ?? ""
            }).ToList();
        }
        _as = Opt(options, "as", new List<string>());
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        void Apply(DataPoint p)
        {
            for (var i = 0; i < _lambdas.Count; i++)
            {
                var result = LambdaEval.Execute(p, _lambdas[i]);
                FlowData.Set(p, i < _as.Count ? _as[i] : $"eval{i}", result);
            }
        }
        if (item is DataItem.Point p) { Apply(p.Value); return Task.FromResult(NodeResult.Emit(item)); }
        if (item is DataItem.Batch b) { foreach (var pt in b.Value.Points) Apply(pt); return Task.FromResult(NodeResult.Emit(item)); }
        return Task.FromResult(NodeResult.OkResult());
    }
}

[FaxeNode("batch")]
public sealed class BatchNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("size", NodeOptionType.Integer, 10L),
        NodeOption.Define("timeout", NodeOptionType.Duration, "5s")
    };

    private int _size = 10;
    private readonly List<DataPoint> _buf = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _size = (int)Opt(options, "size", 10L);
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        if (item is not DataItem.Point p) return Task.FromResult(NodeResult.Emit(item));
        _buf.Add(p.Value.Clone());
        if (_buf.Count < _size) return Task.FromResult(NodeResult.OkResult());
        var batch = new DataBatch { Points = _buf.ToList() };
        batch.SetBounds();
        _buf.Clear();
        return Task.FromResult(NodeResult.Emit(DataItem.From(batch)));
    }
}

[FaxeNode("unbatch")]
public sealed class UnbatchNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => Array.Empty<NodeOption>();

    public override async Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        if (item is DataItem.Batch b)
        {
            foreach (var p in b.Value.Points)
                await EmitAsync(DataItem.From(p), 1, ct);
            return NodeResult.OkResult();
        }
        return NodeResult.Emit(item);
    }
}

[FaxeNode("sample")]
public sealed class SampleNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("every", NodeOptionType.Integer, 2L)
    };

    private long _every = 2;
    private long _count;

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _every = Opt(options, "every", 2L);
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        _count++;
        return Task.FromResult(_count % _every == 0 ? NodeResult.Emit(item) : NodeResult.OkResult());
    }
}

[FaxeNode("log")]
public sealed class LogNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("file", NodeOptionType.String, "faxe.log")
    };

    private string _file = "faxe.log";

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _file = Opt(options, "file", "faxe.log") ?? "faxe.log";
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override async Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        var line = item switch
        {
            DataItem.Point p => FlowData.ToJson(p.Value),
            DataItem.Batch b => $"batch:{b.Value.Points.Count}",
            _ => item.ToString()
        };
        await File.AppendAllTextAsync(_file, line + Environment.NewLine, ct);
        return NodeResult.Emit(item);
    }
}

[FaxeNode("json_emitter")]
public sealed class JsonEmitterNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("every", NodeOptionType.Duration, "1s"),
        NodeOption.Define("json", NodeOptionType.String)
    };

    private long _everyMs = 1000;
    private string _json = "{}";
    private CancellationTokenSource? _cts;

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _everyMs = FaxeTime.DurationToMs(Opt(options, "every", "1s"));
        _json = Opt(options, "json", "{}") ?? "{}";
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task OnInfoAsync(object message, CancellationToken ct)
    {
        if (message is FlowMessages.StartSignal)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Loop(_cts.Token);
        }
        return Task.CompletedTask;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(_everyMs), ct); }
            catch (OperationCanceledException) { break; }

            Dictionary<string, object?> fields;
            try
            {
                fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(_json)
                         ?? new Dictionary<string, object?>();
            }
            catch
            {
                fields = new Dictionary<string, object?>();
            }

            var norm = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in fields)
                norm[k] = FlowData.Normalize(v);

            await EmitAsync(DataItem.From(new DataPoint { Ts = FaxeTime.Now(), Fields = norm }), 1, ct);
        }
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
        => Task.FromResult(NodeResult.OkResult());

    public override Task ShutdownAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
