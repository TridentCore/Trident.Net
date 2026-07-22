namespace TridentCore.Core.Exceptions;

public class BuildArtifactConflictException(string targetPath, BuildArtifactConflictException.ConflictKind kind)
    : Exception($"Build artifact conflict at {targetPath}")
{
    #region Nested type: ConflictKind

    public enum ConflictKind { OccupiedByRegularFileSystemEntry, LegacyImportProjection }

    #endregion

    public string TargetPath { get; } = targetPath;

    public ConflictKind Kind { get; } = kind;
}
