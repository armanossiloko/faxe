namespace Faxe.Core.Data;

/// <summary>
/// Faxe data_batch: ordered list of data_points with optional bounds.
/// </summary>
public sealed class DataBatch
{
    public string? Id { get; set; }
    public List<DataPoint> Points { get; set; } = new();
    public long? Start { get; set; }
    public long? End { get; set; }
    public long? DTag { get; set; }

    public DataBatch Clone()
    {
        return new DataBatch
        {
            Id = Id,
            Points = Points.Select(p => p.Clone()).ToList(),
            Start = Start,
            End = End,
            DTag = DTag
        };
    }

    public void SetBounds()
    {
        if (Points.Count == 0) return;
        Start = Points.Min(p => p.Ts);
        End = Points.Max(p => p.Ts);
    }
}

/// <summary>Union of point or batch flowing through the graph.</summary>
public abstract record DataItem
{
    public sealed record Point(DataPoint Value) : DataItem;
    public sealed record Batch(DataBatch Value) : DataItem;

    public static DataItem From(DataPoint p) => new Point(p);
    public static DataItem From(DataBatch b) => new Batch(b);
}
