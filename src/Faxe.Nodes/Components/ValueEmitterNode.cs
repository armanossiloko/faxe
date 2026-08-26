using Faxe.Core;
using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_value_emitter.</summary>
[FaxeNode("value_emitter")]
public sealed class ValueEmitterNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("every", NodeOptionType.Duration, "5s"),
        NodeOption.Define("jitter", NodeOptionType.Duration, "0ms"),
        NodeOption.Define("type", NodeOptionType.Any, "point"),
        NodeOption.Define("batch_size", NodeOptionType.Integer, 5L),
        NodeOption.Define("align", NodeOptionType.IsSet, false),
        NodeOption.Define("fields", NodeOptionType.BinaryList, new List<string> { "val" }),
        NodeOption.Define("format", NodeOptionType.Atom, null),
        NodeOption.Define("mode", NodeOptionType.String, "random")
    };

    private long _everyMs;
    private long _jitterMs;
    private string _type = "point";
    private int _batchSize = 5;
    private bool _align;
    private List<string> _fields = new() { "val" };
    private string _mode = "random";
    private long _mono;
    private long _dtag;
    private CancellationTokenSource? _loopCts;
    private readonly Random _rng = new();

    public override async Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        await base.InitAsync(nodeId, inputs, options, ct);
        _everyMs = FaxeTime.DurationToMs(Opt(options, "every", "5s"));
        _jitterMs = FaxeTime.DurationToMs(Opt(options, "jitter", "0ms"));
        _type = Opt(options, "type", "point")?.ToString() ?? "point";
        _batchSize = (int)Opt(options, "batch_size", 5L);
        _align = Opt(options, "align", false);
        _fields = Opt(options, "fields", new List<string> { "val" });
        _mode = Opt(options, "mode", "random") ?? "random";
    }

    public override Task OnInfoAsync(object message, CancellationToken ct)
    {
        if (message is FlowMessages.StartSignal)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = LoopAsync(_loopCts.Token);
        }
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = _everyMs + (_jitterMs > 0 ? (long)(_rng.NextDouble() * _jitterMs) : 0);
            try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, delay)), ct); }
            catch (OperationCanceledException) { break; }
            await EmitAsync(Build(), 1, ct);
        }
    }

    private DataItem Build()
    {
        if (string.Equals(_type, "batch", StringComparison.OrdinalIgnoreCase))
        {
            var points = new List<DataPoint>();
            var start = FaxeTime.Now() - ((_batchSize + 1) * _everyMs);
            for (var i = 0; i < _batchSize; i++)
                points.Add(Point(start + i * _everyMs));
            var batch = new DataBatch { Points = points };
            batch.SetBounds();
            return DataItem.From(batch);
        }

        var ts = _align ? FaxeTime.Align(FaxeTime.Now(), _everyMs) : FaxeTime.Now();
        return DataItem.From(Point(ts));
    }

    private DataPoint Point(long ts)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in _fields)
            fields[f] = _mode == "monotonic_int" ? _mono++ : _rng.NextDouble() * 10.0;
        return new DataPoint { Ts = ts, Fields = fields, DTag = _dtag++ };
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
        => Task.FromResult(NodeResult.OkResult());

    public override Task ShutdownAsync(CancellationToken ct)
    {
        _loopCts?.Cancel();
        return Task.CompletedTask;
    }
}
