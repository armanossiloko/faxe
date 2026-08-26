using Faxe.Core.Data;

namespace Faxe.Flow;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FaxeNodeAttribute : Attribute
{
    public FaxeNodeAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>Mirrors df_component callbacks.</summary>
public interface IFaxeNode
{
    IReadOnlyList<NodeOption> Options();
    Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct);
    Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct);
    Task OnInfoAsync(object message, CancellationToken ct) => Task.CompletedTask;
    Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    AutoRequest AutoRequest => AutoRequest.All;
}

public abstract class FaxeNodeBase : IFaxeNode
{
    protected string NodeId { get; private set; } = string.Empty;
    protected FlowNodeContext? Context { get; private set; }

    public abstract IReadOnlyList<NodeOption> Options();

    public virtual Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        NodeId = nodeId;
        return Task.CompletedTask;
    }

    internal void AttachContext(FlowNodeContext context) => Context = context;

    public abstract Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct);

    public virtual Task OnInfoAsync(object message, CancellationToken ct) => Task.CompletedTask;
    public virtual Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    public virtual AutoRequest AutoRequest => AutoRequest.All;

    protected Task EmitAsync(DataItem item, int outPort = 1, CancellationToken ct = default) =>
        Context?.EmitAsync(outPort, item, ct) ?? Task.CompletedTask;

    protected static T Opt<T>(IReadOnlyDictionary<string, object?> options, string key, T fallback)
    {
        if (!options.TryGetValue(key, out var raw) || raw is null)
            return fallback;
        try
        {
            if (raw is T t) return t;
            if (typeof(T) == typeof(string)) return (T)(object)Convert.ToString(raw)!;
            if (typeof(T) == typeof(long)) return (T)(object)Convert.ToInt64(raw);
            if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(raw);
            if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(raw);
            if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(raw);
            if (typeof(T) == typeof(List<string>) && raw is IEnumerable<object?> en)
                return (T)(object)en.Select(x => Convert.ToString(x) ?? string.Empty).ToList();
        }
        catch
        {
            return fallback;
        }
        return fallback;
    }
}

public sealed class FlowNodeContext
{
    private readonly Func<int, DataItem, CancellationToken, Task> _emit;

    public FlowNodeContext(string graphId, string nodeId, Func<int, DataItem, CancellationToken, Task> emit)
    {
        GraphId = graphId;
        NodeId = nodeId;
        _emit = emit;
    }

    public string GraphId { get; }
    public string NodeId { get; }

    public Task EmitAsync(int outPort, DataItem item, CancellationToken ct) => _emit(outPort, item, ct);
}
