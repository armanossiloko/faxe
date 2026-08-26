using System.Collections.Concurrent;
using System.Reflection;
using Faxe.Core.Models;

namespace Faxe.Flow;

public sealed class NodeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);

    public void RegisterAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(IFaxeNode).IsAssignableFrom(type) || type.IsAbstract)
                continue;
            var attr = type.GetCustomAttribute<FaxeNodeAttribute>();
            if (attr is null) continue;
            _types[attr.Name] = type;
        }
    }

    public bool TryGet(string dfsName, out Type type) => _types.TryGetValue(dfsName, out type!);

    public IFaxeNode Create(string dfsName)
    {
        if (!_types.TryGetValue(dfsName, out var type))
            throw new InvalidOperationException($"Unknown node type '{dfsName}'");
        return (IFaxeNode)Activator.CreateInstance(type)!;
    }

    public IReadOnlyList<object> DescribeAll()
    {
        var list = new List<object>();
        foreach (var (name, type) in _types.OrderBy(kv => kv.Key))
        {
            var node = (IFaxeNode)Activator.CreateInstance(type)!;
            list.Add(new
            {
                name,
                options = node.Options().Select(o => new
                {
                    name = o.Name,
                    type = o.Type.ToString().ToLowerInvariant(),
                    @default = o.HasDefault ? o.DefaultValue : null
                })
            });
        }
        return list;
    }

    public IReadOnlyCollection<string> Names => _types.Keys.ToList();
}

public static class OptionBinder
{
    public static Dictionary<string, object?> Bind(IFaxeNode node, IReadOnlyDictionary<string, object?> provided)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var working = new Dictionary<string, object?>(provided, StringComparer.Ordinal);

        if (working.TryGetValue("__positional", out var posRaw) && posRaw is IList<object?> positional)
        {
            working.Remove("__positional");
            var unbound = node.Options().Where(o => !working.ContainsKey(o.Name)).ToList();
            for (var i = 0; i < positional.Count && i < unbound.Count; i++)
                working[unbound[i].Name] = positional[i];
        }

        foreach (var opt in node.Options())
        {
            if (working.TryGetValue(opt.Name, out var value))
                result[opt.Name] = Coerce(opt.Type, value);
            else if (opt.HasDefault)
                result[opt.Name] = opt.DefaultValue;
            else if (opt.Type == NodeOptionType.IsSet)
                result[opt.Name] = false;
            else
                throw new InvalidOperationException($"Missing required option '{opt.Name}' for node");
        }
        foreach (var (k, v) in working)
            if (!result.ContainsKey(k))
                result[k] = v;
        return result;
    }

    public static object? Coerce(NodeOptionType type, object? value)
    {
        if (value is null) return null;
        return type switch
        {
            NodeOptionType.Duration or NodeOptionType.String or NodeOptionType.Binary or NodeOptionType.Atom
                or NodeOptionType.Lambda => Convert.ToString(value),
            NodeOptionType.Integer => Convert.ToInt64(value),
            NodeOptionType.Float or NodeOptionType.Number => Convert.ToDouble(value),
            NodeOptionType.Boolean or NodeOptionType.IsSet => value is bool b ? b : Convert.ToBoolean(value),
            NodeOptionType.BinaryList or NodeOptionType.StringList => ToStringList(value),
            NodeOptionType.IntegerList => ToLongList(value),
            NodeOptionType.FloatList => ToDoubleList(value),
            _ => value
        };
    }

    private static List<string> ToStringList(object value)
    {
        if (value is List<string> ls) return ls;
        if (value is IEnumerable<object?> en) return en.Select(x => Convert.ToString(x) ?? "").ToList();
        return new List<string> { Convert.ToString(value) ?? "" };
    }

    private static List<long> ToLongList(object value) =>
        value is IEnumerable<object?> en
            ? en.Select(x => Convert.ToInt64(x)).ToList()
            : new List<long> { Convert.ToInt64(value) };

    private static List<double> ToDoubleList(object value) =>
        value is IEnumerable<object?> en
            ? en.Select(x => Convert.ToDouble(x)).ToList()
            : new List<double> { Convert.ToDouble(value) };
}
