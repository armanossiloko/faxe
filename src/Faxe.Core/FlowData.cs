using System.Collections;
using System.Globalization;
using System.Text.Json;
using Faxe.Core.Data;

namespace Faxe.Core;

/// <summary>
/// JSON-path like field access used throughout Faxe (e.g. averages.emitted[5], axis.z.cur).
/// </summary>
public static class FlowData
{
    public static object? Get(DataPoint point, string path) => GetFromMap(point.Fields, path);
    public static object? GetTag(DataPoint point, string path) => GetFromMap(point.Tags, path);

    public static void Set(DataPoint point, string path, object? value) =>
        SetInMap(point.Fields, path, value);

    public static void SetTag(DataPoint point, string path, object? value) =>
        SetInMap(point.Tags, path, value);

    public static void Delete(DataPoint point, string path) => DeleteFromMap(point.Fields, path);

    public static void Keep(DataPoint point, IEnumerable<string> paths)
    {
        var keep = new HashSet<string>(paths, StringComparer.Ordinal);
        var next = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var path in keep)
        {
            var v = Get(point, path);
            if (v is not null || PathExists(point.Fields, path))
                SetInMap(next, path, v);
        }
        point.Fields = next;
    }

    public static void Rename(DataPoint point, string from, string to)
    {
        var v = Get(point, from);
        Delete(point, from);
        Set(point, to, v);
    }

    public static bool PathExists(IDictionary<string, object?> map, string path)
    {
        try
        {
            _ = GetFromMap(map, path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static object? GetFromMap(IDictionary<string, object?> map, string path)
    {
        var parts = ParsePath(path);
        object? current = map;
        foreach (var part in parts)
        {
            if (part.Index is int idx)
            {
                if (current is not IList list || idx < 0 || idx >= list.Count)
                    return null;
                current = list[idx];
            }
            else
            {
                if (current is not IDictionary<string, object?> dict ||
                    !dict.TryGetValue(part.Key!, out current))
                    return null;
            }
        }
        return current;
    }

    public static void SetInMap(IDictionary<string, object?> map, string path, object? value)
    {
        var parts = ParsePath(path);
        IDictionary<string, object?> current = map;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];
            if (part.Index is not null)
                throw new InvalidOperationException($"Cannot set through list index in path '{path}' without existing list scaffolding.");

            if (!current.TryGetValue(part.Key!, out var next) || next is not IDictionary<string, object?> child)
            {
                child = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[part.Key!] = child;
            }
            current = child;
        }

        var last = parts[^1];
        if (last.Index is int)
            throw new InvalidOperationException($"Setting list indices not supported for '{path}'.");
        current[last.Key!] = value;
    }

    public static void DeleteFromMap(IDictionary<string, object?> map, string path)
    {
        var parts = ParsePath(path);
        IDictionary<string, object?> current = map;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];
            if (part.Index is not null) return;
            if (!current.TryGetValue(part.Key!, out var next) || next is not IDictionary<string, object?> child)
                return;
            current = child;
        }
        var last = parts[^1];
        if (last.Key is not null)
            current.Remove(last.Key);
    }

    public static List<PathPart> ParsePath(string path)
    {
        var parts = new List<PathPart>();
        var i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                continue;
            }

            if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0) throw new FormatException($"Invalid path '{path}'");
                var idxText = path[(i + 1)..end];
                parts.Add(new PathPart(null, int.Parse(idxText, CultureInfo.InvariantCulture)));
                i = end + 1;
                continue;
            }

            var start = i;
            while (i < path.Length && path[i] is not ('.' or '['))
                i++;
            parts.Add(new PathPart(path[start..i], null));
        }
        return parts;
    }

    public readonly record struct PathPart(string? Key, int? Index);

    public static string ToJson(DataPoint point) =>
        JsonSerializer.Serialize(new
        {
            ts = point.Ts,
            fields = Normalize(point.Fields),
            tags = Normalize(point.Tags),
            dtag = point.DTag,
            id = point.Id
        });

    public static object? Normalize(object? value) => value switch
    {
        null => null,
        Dictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value)),
        IDictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value)),
        IList list => list.Cast<object?>().Select(Normalize).ToList(),
        JsonElement el => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        },
        _ => value
    };
}
