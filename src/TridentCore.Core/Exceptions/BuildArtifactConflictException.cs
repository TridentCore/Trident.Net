namespace TridentCore.Core.Exceptions;

public class BuildArtifactConflictException(
    string targetPath,
    BuildArtifactConflictException.ConflictKind kind
) : Exception($"Build artifact conflict at {targetPath}")
{
    public string TargetPath { get; } = targetPath;

    public ConflictKind Kind { get; } = kind;

    #region Nested type: ConflictKind

    public enum ConflictKind
    {
        OccupiedByRegularFileSystemEntry,
        LegacyImportProjection,
    }

    #endregion
}
