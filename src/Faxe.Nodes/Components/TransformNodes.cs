using Faxe.Core;
using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

[FaxeNode("debug")]
public sealed class DebugNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => Array.Empty<NodeOption>();

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        var text = item switch
        {
            DataItem.Point p => FlowData.ToJson(p.Value),
            DataItem.Batch b => $"batch[{b.Value.Points.Count}]",
            _ => item.ToString()
        };
        Console.WriteLine($"[debug:{NodeId}] {text}");
        return Task.FromResult(NodeResult.Emit(item));
    }
}

[FaxeNode("delete")]
public sealed class DeleteNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.BinaryList, new List<string>()),
        NodeOption.Define("tags", NodeOptionType.BinaryList, new List<string>())
    };

    private List<string> _fields = new();
    private List<string> _tags = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _fields = Opt(options, "fields", new List<string>());
        _tags = Opt(options, "tags", new List<string>());
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        if (item is DataItem.Point p)
        {
            foreach (var f in _fields) FlowData.Delete(p.Value, f);
            foreach (var t in _tags) FlowData.DeleteFromMap(p.Value.Tags, t);
            return Task.FromResult(NodeResult.Emit(item));
        }
        if (item is DataItem.Batch b)
        {
            foreach (var pt in b.Value.Points)
            {
                foreach (var f in _fields) FlowData.Delete(pt, f);
                foreach (var t in _tags) FlowData.DeleteFromMap(pt.Tags, t);
            }
            return Task.FromResult(NodeResult.Emit(item));
        }
        return Task.FromResult(NodeResult.OkResult());
    }
}

[FaxeNode("keep")]
public sealed class KeepNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.BinaryList)
    };

    private List<string> _fields = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _fields = Opt(options, "fields", new List<string>());
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        if (item is DataItem.Point p)
        {
            FlowData.Keep(p.Value, _fields);
            return Task.FromResult(NodeResult.Emit(item));
        }
        if (item is DataItem.Batch b)
        {
            foreach (var pt in b.Value.Points) FlowData.Keep(pt, _fields);
            return Task.FromResult(NodeResult.Emit(item));
        }
        return Task.FromResult(NodeResult.OkResult());
    }
}

[FaxeNode("default")]
public sealed class DefaultNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("fields", NodeOptionType.BinaryList),
        NodeOption.Define("field_values", NodeOptionType.List),
        NodeOption.Define("tags", NodeOptionType.BinaryList, new List<string>()),
        NodeOption.Define("tag_values", NodeOptionType.List, new List<object>())
    };

    private List<string> _fields = new();
    private List<object?> _fieldValues = new();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _fields = Opt(options, "fields", new List<string>());
        _fieldValues = options.TryGetValue("field_values", out var fv) && fv is IEnumerable<object?> en
            ? en.Cast<object?>().ToList()
            : new List<object?>();
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
    {
        void Apply(DataPoint p)
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                if (FlowData.Get(p, _fields[i]) is null)
                    FlowData.Set(p, _fields[i], i < _fieldValues.Count ? _fieldValues[i] : null);
            }
        }
        if (item is DataItem.Point p) { Apply(p.Value); return Task.FromResult(NodeResult.Emit(item)); }
        if (item is DataItem.Batch b) { foreach (var pt in b.Value.Points) Apply(pt); return Task.FromResult(NodeResult.Emit(item)); }
        return Task.FromResult(NodeResult.OkResult());
    }
}
