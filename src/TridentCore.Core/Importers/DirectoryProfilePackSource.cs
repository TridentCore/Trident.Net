using TridentCore.Abstractions.Importers;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public sealed class DirectoryProfilePackSource(string directory) : IProfilePackSource
{
    private readonly string _directory = Path.GetFullPath(directory);
    private IReadOnlyList<string>? _fileNames;

    public IReadOnlyList<string> FileNames => _fileNames ??=
        Directory.EnumerateFiles(_directory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        }).Select(x => Path.GetRelativePath(_directory, x).Replace('\\', '/')).ToArray();

    public Stream Open(string fileName) => File.OpenRead(PatchHelper.ResolvePath(_directory, fileName));

    public long? LengthOf(string fileName)
    {
        var file = new FileInfo(PatchHelper.ResolvePath(_directory, fileName));
        return file.Exists ? file.Length : null;
    }
}
