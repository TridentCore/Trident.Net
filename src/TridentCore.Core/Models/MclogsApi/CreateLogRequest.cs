namespace TridentCore.Core.Models.MclogsApi;

public record CreateLogRequest(string Content, string? Source, IReadOnlyList<MclogsMetadataEntry>? Metadata);
