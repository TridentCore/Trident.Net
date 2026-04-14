namespace Trident.Core.Models.MclogsApi;

public record CreateLogRequest(
    string Content,
    string? Source,
    IReadOnlyList<MclogsMetadataEntry>? Metadata
);

public record MclogsMetadataEntry(string Key, string? Value, string? Label, bool Visible = true);
