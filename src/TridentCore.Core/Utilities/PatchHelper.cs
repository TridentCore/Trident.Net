using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Utilities;

public static class PatchHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new(FileHelper.SerializerOptions)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static string Fingerprint<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, FileHelper.SerializerOptions)));

    public static string ResolvePath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length == 0 || parts.Any(x => x is "" or "." or ".." || x.Contains(':')))
        {
            throw new InvalidDataException($"Invalid patch path '{relative}'.");
        }

        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!FileHelper.IsInDirectory(path, Path.GetFullPath(root)))
        {
            throw new InvalidDataException($"Patch path '{relative}' escapes its directory.");
        }

        var current = Path.GetFullPath(root);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Patch path '{relative}' contains a symbolic link.");
            }
        }
        return path;
    }

    public static bool Matches(IReadOnlyList<PatchDocument.PlatformRule> rules)
    {
        if (rules.Count == 0)
        {
            return true;
        }

        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var allowed = false;
        foreach (var rule in rules)
        {
            if (rule.Action is not ("allow" or "disallow"))
            {
                throw new InvalidDataException($"Unknown platform rule '{rule.Action}'.");
            }
            var matches = (rule.Os is null || rule.Os == os || rule.Os == $"{os}-{arch}" || (arch == "arm" && rule.Os == $"{os}-arm32"))
                       && (rule.Arch is null || Regex.IsMatch(arch, rule.Arch, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                       && (rule.Version is null || Regex.IsMatch(Environment.OSVersion.Version.ToString(), rule.Version, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            if (matches)
            {
                allowed = rule.Action == "allow";
            }
        }
        return allowed;
    }

    public static LockData.Library.Identity ParseIdentity(string identity)
    {
        var extension = "jar";
        var suffix = identity.IndexOf('@');
        if (suffix >= 0)
        {
            extension = identity[(suffix + 1)..];
            identity = identity[..suffix];
        }
        var parts = identity.Split(':');
        if (parts.Length is not (3 or 4) || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"Invalid library identity '{identity}'.");
        }
        foreach (var part in parts.Append(extension))
        {
            if (string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new InvalidDataException($"Invalid library identity '{identity}'.");
            }
        }
        return new(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : null, extension);
    }

    public static string Identity(LockData.Library.Identity id) =>
        $"{id.Namespace}:{id.Name}:{id.Version}{(id.Platform is null ? "" : ":" + id.Platform)}{(id.Extension == "jar" ? "" : "@" + id.Extension)}";

    public static int CompareVersions(string left, string right)
    {
        var a = Regex.Matches(left, "[0-9]+|[^0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(x => x.Value).ToArray();
        var b = Regex.Matches(right, "[0-9]+|[^0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(x => x.Value).ToArray();
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var comparison = System.Numerics.BigInteger.TryParse(a[i], out var av) && System.Numerics.BigInteger.TryParse(b[i], out var bv)
                                 ? av.CompareTo(bv)
                                 : string.Compare(a[i], b[i], StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return a.Length.CompareTo(b.Length);
    }

}
