using Faxe.Core.Data;
using Faxe.Flow;

namespace Faxe.Nodes.Components;

/// <summary>Port of esp_blobstream</summary>
[FaxeNode("blobstream")]
public sealed class BlobstreamNode : FaxeNodeBase
{
    public override IReadOnlyList<NodeOption> Options() => new[]
    {
        NodeOption.Define("account_url", NodeOptionType.String, null),
        NodeOption.Define("az_sec", NodeOptionType.String, null),
        NodeOption.Define("container", NodeOptionType.String, "test"),
        NodeOption.Define("blob_name", NodeOptionType.String, "4ed182c6eb9e"),
        NodeOption.Define("encoding", NodeOptionType.String, "utf-8"),
        NodeOption.Define("chunk_size", NodeOptionType.Integer, 4096L),
        NodeOption.Define("format", NodeOptionType.String, null),
        NodeOption.Define("header_row", NodeOptionType.Integer, 1L),
        NodeOption.Define("data_start_row", NodeOptionType.Integer, 2L),
        NodeOption.Define("date_field", NodeOptionType.String, "date"),
        NodeOption.Define("date_format", NodeOptionType.String, "Y-m-D H:M:s"),
        NodeOption.Define("line_separator", NodeOptionType.String, "\\n"),
        NodeOption.Define("column_separator", NodeOptionType.String, ","),
        NodeOption.Define("batch_size", NodeOptionType.Integer, 120L),
        NodeOption.Define("opts_field", NodeOptionType.String, null),
        NodeOption.Define("retries", NodeOptionType.Integer, 3L),
    };

    private IReadOnlyDictionary<string, object?> _opts = new Dictionary<string, object?>();

    public override Task InitAsync(string nodeId, IReadOnlyList<int> inputs, IReadOnlyDictionary<string, object?> options, CancellationToken ct)
    {
        _opts = options;
        return base.InitAsync(nodeId, inputs, options, ct);
    }

    public override Task<NodeResult> ProcessAsync(int inPort, DataItem item, CancellationToken ct)
        => Task.FromResult(NodeResult.Emit(item));
}
