using Trident.Abstractions;

namespace Trident.Cli.Services;

public static class CliDataPaths
{
    public const string Brand = "trident.cli";

    public static string PrivateDirectory
    {
        get
        {
            var directory = PathDef.Default.PrivateDirectory(Brand);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string File(string fileName) => Path.Combine(PrivateDirectory, fileName);
}
