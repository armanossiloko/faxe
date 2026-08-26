using Faxe.Core.Data;

namespace Faxe.Flow;

public enum NodeOptionType
{
    Duration,
    Integer,
    Float,
    Number,
    Boolean,
    IsSet,
    String,
    Binary,
    Atom,
    Any,
    Lambda,
    BinaryList,
    StringList,
    List,
    IntegerList,
    FloatList
}

public sealed class NodeOption
{
    public string Name { get; init; } = string.Empty;
    public NodeOptionType Type { get; init; }
    public object? DefaultValue { get; init; }
    public bool HasDefault { get; init; }

    public static NodeOption Define(string name, NodeOptionType type) =>
        new() { Name = name, Type = type, HasDefault = false };

    public static NodeOption Define(string name, NodeOptionType type, object? defaultValue) =>
        new() { Name = name, Type = type, DefaultValue = defaultValue, HasDefault = true };
}

public enum AutoRequest
{
    All,
    Emit,
    None
}

public abstract class NodeResult
{
    public sealed class Ok : NodeResult
    {
        public static readonly Ok Instance = new();
    }

    public sealed class EmitItem : NodeResult
    {
        public EmitItem(DataItem item, int outPort = 1)
        {
            Item = item;
            OutPort = outPort;
        }

        public DataItem Item { get; }
        public int OutPort { get; }
    }

    public sealed class Error : NodeResult
    {
        public Error(string reason) => Reason = reason;
        public string Reason { get; }
    }

    public static NodeResult OkResult() => Ok.Instance;
    public static NodeResult Emit(DataItem item, int outPort = 1) => new EmitItem(item, outPort);
    public static NodeResult Fail(string reason) => new Error(reason);
}
