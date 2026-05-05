namespace TridentCore.Abstractions.Snapshots;

public record ReferenceInfo(object Id, string Hash, string RelativePath, long Size, DateTime LastModified)

{
}
