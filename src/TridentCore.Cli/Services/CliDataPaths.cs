using TridentCore.Abstractions;

namespace TridentCore.Cli.Services;

public static class CliDataPaths
{
    public static string PrivateDirectory
    {
        get
        {
            var directory = PathDef.Default.PrivateConfigDirectory();
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string File(string fileName) => Path.Combine(PrivateDirectory, fileName);
}
