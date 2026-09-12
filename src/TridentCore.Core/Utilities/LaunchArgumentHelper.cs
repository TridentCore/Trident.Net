using System.Text.RegularExpressions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Extensions;

namespace TridentCore.Core.Utilities;

public static class LaunchArgumentHelper
{
    private static readonly Regex FILE_REFERENCE = new(@"\$\{(main_jar|forge_installer)\}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Expand(string argument, LockData.ArtifactData artifact, string? key) =>
        FILE_REFERENCE.Replace(argument, match => Resolve(match.Groups[1].Value, artifact).FilePath(key));

    private static LockData.Library Resolve(string reference, LockData.ArtifactData artifact)
    {
        if (reference == "main_jar")
        {
            return artifact.MainJar ?? throw new InvalidDataException("The main_jar argument reference requires a main JAR.");
        }

        var installers = artifact.Libraries.Where(x => x is
        {
            IsNative: false,
            IsPresent: false,
            Id: { Namespace: "net.minecraftforge" or "net.neoforged", Name: "forge" or "neoforge", Platform: "installer", Extension: "jar" }
        }).ToArray();
        return installers.Length == 1
                   ? installers[0]
                   : throw new InvalidDataException($"The forge_installer argument reference requires exactly one installer library; found {installers.Length}.");
    }
}
