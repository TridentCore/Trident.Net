using System.Text.RegularExpressions;
using TridentCore.Abstractions.Launching;

namespace TridentCore.Abstractions.Utilities;

public static partial class ArtifactHelper
{
    [GeneratedRegex(@"\$\{artifact:([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    public static string ReferenceOf(LaunchArtifact.Identity identity) => "${artifact:" + CoordinateOf(identity) + "}";

    public static string BindReferences(string value, Func<LaunchArtifact.Identity, string> bind) =>
        ReferencePattern().Replace(value, match => bind(Parse(match.Groups[1].Value)));

    public static LaunchArtifact.Identity Parse(string coordinate)
    {
        var parts = coordinate.Split('@');
        if (parts.Length > 2)
        {
            throw new FormatException($"Invalid artifact coordinate '{coordinate}'");
        }

        var fields = parts[0].Split(':');
        if (fields.Length is not (3 or 4))
        {
            throw new FormatException($"Invalid artifact coordinate '{coordinate}'");
        }

        var identity = new LaunchArtifact.Identity(fields[0], fields[1], fields[2],
                                                    fields.Length == 4 ? fields[3] : null,
                                                    parts.Length == 2 ? parts[1] : "jar");
        Validate(identity);
        return identity;
    }

    public static void Validate(LaunchArtifact.Identity identity)
    {
        if (!IsSafeSegment(identity.Namespace) || identity.Namespace.Split('.').Any(string.IsNullOrEmpty)
         || !IsSafeSegment(identity.Name) || !IsSafeSegment(identity.Version)
         || (identity.Classifier is not null && !IsSafeSegment(identity.Classifier))
         || !IsSafeSegment(identity.Extension))
        {
            throw new FormatException($"Invalid artifact identity '{CoordinateOf(identity)}'");
        }
    }

    public static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "." and not ".."
     && !value.EndsWith('.')
     && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "/\\:<>\"|?*%@#".Contains(c));

    public static string CoordinateOf(LaunchArtifact.Identity identity) =>
        $"{identity.Namespace}:{identity.Name}:{identity.Version}"
      + (identity.Classifier is null ? "" : $":{identity.Classifier}")
      + (identity.Extension == "jar" ? "" : $"@{identity.Extension}");

    public static string FileNameOf(LaunchArtifact.Identity identity) =>
        $"{identity.Name}-{identity.Version}"
      + (identity.Classifier is null ? "" : $"-{identity.Classifier}")
      + $".{identity.Extension}";

    public static Uri ResolveRepository(Uri repository, LaunchArtifact.Identity identity)
    {
        Validate(identity);
        var root = repository.AbsoluteUri.EndsWith('/') ? repository : new Uri(repository.AbsoluteUri + '/');
        var segments = identity.Namespace.Split('.').Concat([identity.Name, identity.Version, FileNameOf(identity)]);
        return new(root, string.Join('/', segments.Select(Uri.EscapeDataString)));
    }

    public static string LocationOf(LaunchArtifact artifact) => artifact.Url.IsFile
        ? artifact.Url.LocalPath
        : PathDef.Default.FileOfLibrary(artifact.Id.Namespace, artifact.Id.Name, artifact.Id.Version,
                                        artifact.Id.Classifier, artifact.Id.Extension);
}
