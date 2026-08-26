using System.Collections;
using System.Text.Json.Serialization;

namespace Faxe.Core.Data;

/// <summary>
/// Faxe data_point: timestamp (unix ms), fields, tags, optional delivery tag and id.
/// </summary>
public sealed class DataPoint
{
    public long Ts { get; set; }

    public Dictionary<string, object?> Fields { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Tags { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DTag { get; set; }

    public string Id { get; set; } = string.Empty;

    public DataPoint Clone()
    {
        return new DataPoint
        {
            Ts = Ts,
            Fields = DeepCloneMap(Fields),
            Tags = DeepCloneMap(Tags),
            DTag = DTag,
            Id = Id
        };
    }

    public static Dictionary<string, object?> DeepCloneMap(Dictionary<string, object?> source)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in source)
            copy[k] = DeepCloneValue(v);
        return copy;
    }

    public static object? DeepCloneValue(object? value) => value switch
    {
        null => null,
        Dictionary<string, object?> map => DeepCloneMap(map),
        IDictionary<string, object?> map => DeepCloneMap(new Dictionary<string, object?>(map, StringComparer.Ordinal)),
        IList<object?> list => list.Select(DeepCloneValue).ToList(),
        IList list => list.Cast<object?>().Select(DeepCloneValue).ToList(),
        _ => value
    };
}
