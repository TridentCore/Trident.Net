using System.Text;
using System.Text.Json;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Services;

namespace TridentCore.Core.Utilities;

public static class MetadataComponentHelper
{
    private static readonly Uri DEFAULT_REPOSITORY = new("https://libraries.minecraft.net/");

    public static Conversion Convert(Component source, string id, string? version, string minecraftVersion)
    {
        if (source.FormatVersion != 1 || (source.Uid.Length > 0 && source.Uid != id))
        {
            throw new FormatException($"Unsupported metadata format or mismatched identity for component '{id}'");
        }
        if (source.JarMods is { Count: > 0 })
        {
            throw new NotSupportedException($"Component '{id}' requires client jar modification, which is not supported");
        }
        foreach (var key in source.Extra?.Keys ?? Enumerable.Empty<string>())
        {
            if (key.StartsWith('+') || key.StartsWith('-'))
            {
                throw new NotSupportedException($"Component '{id}' uses unsupported launch field '{key}'");
            }
        }

        bool? firstThread = null;
        foreach (var trait in source.Traits ?? [])
        {
            if (trait == "FirstThreadOnMacOS")
            {
                firstThread = true;
            }
            else
            {
                throw new NotSupportedException($"Component '{id}' requires unsupported launch trait '{trait}'");
            }
        }

        var localFiles = new Dictionary<string, LocalFile>(StringComparer.Ordinal);
        var libraries = new List<LaunchLibrary>();
        foreach (var library in (source.Libraries ?? []).Concat(source.AdditionalLibraries ?? []))
        {
            AddLibrary(library, LaunchLibrary.Usage.Classpath);
        }
        foreach (var library in source.MavenFiles ?? [])
        {
            AddLibrary(library, LaunchLibrary.Usage.Required);
        }
        foreach (var library in source.Agents ?? [])
        {
            AddLibrary(library, LaunchLibrary.Usage.Agent);
        }
        if (source.MainJar is { } client)
        {
            AddLibrary(client, LaunchLibrary.Usage.Client);
        }

        var game = new LaunchArguments
        {
            Replace = source.MinecraftArguments is null ? null
                : SplitArguments(source.MinecraftArguments).Select(x => new LaunchArgument(x)).ToArray(),
            Append = (source.Tweakers ?? []).SelectMany(x => new[]
                { new LaunchArgument("--tweakClass"), new LaunchArgument(x) }).ToArray()
        };
        var java = new LaunchArguments
        {
            Append = (source.JvmArguments ?? []).Select(x => new LaunchArgument(x)).ToArray()
        };
        if (source.Arguments is { } arguments)
        {
            foreach (var key in arguments.Keys)
            {
                if (key is not ("game" or "jvm"))
                {
                    throw new NotSupportedException($"Component '{id}' has unsupported argument section '{key}'");
                }
            }
            if (arguments.TryGetValue("game", out var gameValues))
            {
                game = game with { Replace = ConvertArguments(gameValues) };
            }
            if (arguments.TryGetValue("jvm", out var javaValues))
            {
                java = java with { Append = [.. ConvertArguments(javaValues), .. java.Append] };
            }
        }

        if (source.MainClass == "io.github.zekerzhayard.forgewrapper.installer.Main")
        {
            var installer = libraries.SingleOrDefault(x => x.Artifact.Id.Classifier == "installer")
                            ?? throw new FormatException($"Component '{id}' has no installer artifact");
            java = java with
            {
                Append = [.. java.Append,
                    new("-Dforgewrapper.librariesDir=${library_directory}"),
                    new("-Dforgewrapper.installer=${artifact:" + ArtifactHelper.CoordinateOf(installer.Artifact.Id) + "}"),
                    new("-Dforgewrapper.minecraft=${minecraft_jar}")]
            };
        }

        LaunchAssetIndex? assets = null;
        if (source.AssetIndex is { } assetIndex)
        {
            var uri = assetIndex.Url;
            if (!uri.IsAbsoluteUri)
            {
                var path = SafeArchivePath(uri.OriginalString);
                uri = RegisterLocal(path, "assets/" + Path.GetFileName(path));
            }
            assets = new(assetIndex.Id, uri, FileHash.FromSha1(assetIndex.Sha1));
        }

        var component = new LaunchComponent
        {
            Id = id, Version = source.Version ?? version,
            Requires = source.Requires.Select(x => new LaunchRequirement(x.Uid, x.Equal,
                x.Suggest ?? (x.Uid == PrismLauncherService.UID_INTERMEDIARY ? minecraftVersion : null))).ToArray(),
            Conflicts = source.Conflicts.Select(x => new LaunchRequirement(x.Uid, x.Equal)).ToArray(),
            MainClass = source.MainClass, StartOnFirstThread = firstThread,
            JavaMajors = source.CompatibleJavaMajors ?? (id == PrismLauncherService.UID_MINECRAFT ? [8u] : null),
            AssetIndex = assets, GameArguments = game, JavaArguments = java, Libraries = libraries
        };
        LaunchDefinitionHelper.Validate(component, id);
        return new(component, [.. localFiles.Values]);

        Uri RegisterLocal(string archivePath, string fileName)
        {
            var target = "files/" + SafeArchivePath(fileName);
            if (localFiles.TryGetValue(target, out var existing) && existing.ArchivePath != archivePath)
            {
                throw new FormatException($"Component '{id}' has conflicting local files at '{target}'");
            }
            localFiles[target] = new(archivePath, target);
            return new("../" + target, UriKind.Relative);
        }

        LaunchArtifact ResolveArtifact(Component.Library library, LaunchArtifact.Identity identity,
                                       Component.Library.DownloadsEntry.ArtifactEntry? download)
        {
            if (library.Hint == MultiMcHelper.LIBRARY_HINT_LOCAL)
            {
                var name = library.FileName ?? ArtifactHelper.FileNameOf(identity);
                if (!ArtifactHelper.IsSafeSegment(name))
                {
                    throw new FormatException($"Unsafe local library filename '{name}'");
                }
                return new(identity, RegisterLocal($"libraries/{name}", name));
            }
            if (library.Hint is not null && library.Hint != "always-stale")
            {
                throw new NotSupportedException($"Unsupported library hint '{library.Hint}' in '{id}'");
            }
            if (library.Hint == "always-stale")
            {
                throw new NotSupportedException($"Component '{id}' requires an always-stale artifact source");
            }
            if (download is not null)
            {
                return new(identity, download.Url, FileHash.FromSha1(download.Sha1));
            }
            if ((library.AbsoluteUrl ?? library.LegacyAbsoluteUrl) is { } absolute)
            {
                if (!absolute.IsAbsoluteUri || absolute.IsFile)
                {
                    throw new FormatException($"Invalid absolute download URL for '{library.Name}'");
                }
                return new(identity, absolute);
            }
            return new(identity, ArtifactHelper.ResolveRepository(library.Url ?? DEFAULT_REPOSITORY, identity));
        }

        void AddLibrary(Component.Library library, LaunchLibrary.Usage use)
        {
            var identity = ArtifactHelper.Parse(library.Name);
            var rules = ConvertRules(library.Rules);
            if (library.Downloads?.Artifact is not null || library.Natives is not { Count: > 0 })
            {
                libraries.Add(new(ResolveArtifact(library, identity, library.Downloads?.Artifact), use) { Rules = rules });
            }
            if (library.Natives is not { Count: > 0 })
            {
                return;
            }
            if (use is not LaunchLibrary.Usage.Classpath)
            {
                throw new NotSupportedException($"Native mappings on a '{use}' artifact are not supported: '{library.Name}'");
            }
            var mappings = new Dictionary<(string Os, string? Architecture), string>();
            foreach (var (key, classifier) in library.Natives)
            {
                var parts = key.Split('-', 2);
                if (parts[0] is not ("windows" or "linux" or "osx"))
                {
                    throw new NotSupportedException($"Unsupported native platform '{key}'");
                }
                var architecture = parts.Length == 2 ? LaunchRuleHelper.NormalizeArchitecture(parts[1]) : null;
                if (!mappings.TryAdd((parts[0], architecture), classifier))
                {
                    throw new FormatException($"Duplicate native platform mapping '{key}' in '{library.Name}'");
                }
            }
            foreach (var os in new[] { "windows", "linux", "osx" })
            foreach (var architecture in new[] { "x86", "x64", "arm", "arm64" })
            {
                if (!mappings.TryGetValue((os, architecture), out var classifier)
                 && !mappings.TryGetValue((os, null), out classifier))
                {
                    continue;
                }
                classifier = classifier.Replace("${arch}", architecture is "x86" or "arm" ? "32" : "64");
                var nativeId = identity with { Classifier = classifier };
                Component.Library.DownloadsEntry.ArtifactEntry? download = null;
                if (library.Downloads is { } downloads && !downloads.Classifiers.TryGetValue(classifier, out download))
                {
                    if (rules.Any(x => x.OsVersion is not null)
                     || LaunchRuleHelper.Allows(rules, new(os, architecture, "", 0)))
                    {
                        throw new FormatException($"Library '{library.Name}' maps '{os}-{architecture}' to missing classifier '{classifier}'");
                    }
                    continue;
                }
                libraries.Add(new(ResolveArtifact(library, nativeId, download), LaunchLibrary.Usage.Native)
                {
                    Rules = rules, Platform = new(os, architecture), ExtractExcludes = library.Extract?.Exclude ?? []
                });
            }
        }
    }

    public static IReadOnlyList<LaunchRule> ConvertRules(IReadOnlyList<Component.Library.Rule>? rules) =>
        (rules ?? []).Select(rule =>
        {
            if (rule.Features is { Count: > 0 })
            {
                throw new NotSupportedException("Feature-dependent launch rules are not supported");
            }
            if (rule.Action is not ("allow" or "disallow"))
            {
                throw new FormatException($"Unknown library rule action '{rule.Action}'");
            }
            string? Value(string key) => rule.Os?.GetValueOrDefault(key);
            var architecture = Value("arch");
            if (architecture is "amd64" or "x86_64" or "aarch64" or "i386" or "arm32")
            {
                architecture = LaunchRuleHelper.NormalizeArchitecture(architecture);
            }
            return new LaunchRule(rule.Action == "allow", Value("name"), architecture, Value("version"));
        }).ToArray();

    private static IReadOnlyList<LaunchArgument> ConvertArguments(IEnumerable<JsonElement> values)
    {
        var result = new List<LaunchArgument>();
        foreach (var value in values)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                result.Add(new(value.GetString()!));
                continue;
            }
            var rules = value.TryGetProperty("rules", out var ruleNode)
                ? ConvertRules(ruleNode.Deserialize<Component.Library.Rule[]>(JsonSerializerOptions.Web)) : [];
            var argument = value.GetProperty("value");
            foreach (var text in argument.ValueKind == JsonValueKind.Array
                         ? argument.EnumerateArray().Select(x => x.GetString()!) : new[] { argument.GetString()! })
            {
                result.Add(new(text, rules));
            }
        }
        return result;
    }

    public static IReadOnlyList<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length && (value[i + 1] == quote || value[i + 1] == '\\'))
            {
                current.Append(value[++i]);
                started = true;
            }
            else if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c is '\'' or '"')
            {
                quote = c;
                started = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (started)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (quote != '\0') throw new FormatException("Unterminated quote in launch arguments");
        if (started) result.Add(current.ToString());
        return result;
    }

    private static string SafeArchivePath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Split('/').Any(x => !ArtifactHelper.IsSafeSegment(x)))
        {
            throw new FormatException($"Unsafe archive path '{path}'");
        }
        return path;
    }

    public sealed record LocalFile(string ArchivePath, string Target);
    public sealed record Conversion(LaunchComponent Component, IReadOnlyList<LocalFile> LocalFiles);
}
