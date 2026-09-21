namespace TridentCore.Core.Exceptions;

public class BuildArtifactConflictException(string targetPath, BuildArtifactConflictException.ConflictKind kind)
    : Exception($"Build artifact conflict at {targetPath}")
{
    #region Nested type: ConflictKind

    public enum ConflictKind { OccupiedByRegularFileSystemEntry }

    #endregion

    public string TargetPath { get; } = targetPath;

    public ConflictKind Kind { get; } = kind;

    public static BuildArtifactConflictException Occupied(string targetPath) =>
        new(targetPath, ConflictKind.OccupiedByRegularFileSystemEntry);
}
