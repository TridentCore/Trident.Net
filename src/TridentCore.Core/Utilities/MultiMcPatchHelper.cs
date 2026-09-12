using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Services;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Utilities;

// 将源实例自带的组件定义转换成原生 Patch；未提供定义的组件仍由标准提供方处理。
public static class MultiMcPatchHelper
{
    // NOTE: 改写客户端 JAR 的声明用 patch 无法复现，宁可明确失败也不静默产出一个错的实例。
    //  其余已被现代 Prism 判定为失效的字段（tweakers、-libraries、-tweakers、±minecraftArguments）
    //  在源启动器里同样不参与启动，忽略即可——它们落在 JsonExtensionData 里。
    private static readonly string[] JAR_MODIFICATION_FIELDS = ["jarMods", "+jarMods"];

    public record Result(IReadOnlyList<(string Source, string Target)> Files, IReadOnlyDictionary<string, byte[]> Generated);

    // 读取归档自带的组件定义，按 mmc-pack.json 的启用状态与顺序校验。不存在定义的组件不出现在结果里，
    //  由调用方交给标准产出阶段。
    public static async Task<IReadOnlyDictionary<string, Component>> ReadDefinitionsAsync(
        IProfilePackSource pack,
        MmcPack manifest)
    {
        if (manifest.FormatVersion != 1)
        {
            throw new NotSupportedException($"Unsupported instance format {manifest.FormatVersion}.");
        }

        // 禁用组件不参与导入：它们不贡献启动数据，也不该由导入器替用户决定重新启用。
        var entries = manifest.Components.Where(x => !x.Disabled).ToList();
        if (entries.Select(x => x.Uid).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new InvalidDataException("The instance contains duplicate component identifiers.");
        }

        var definitions = new Dictionary<string, Component>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ValidateUid(entry.Uid);
            if (entry.Uid is "_trident_base" or "_trident_arguments")
            {
                throw new InvalidDataException("Reserved patch identifier.");
            }

            var path = $"patches/{entry.Uid}.json";
            if (!pack.FileNames.Contains(path))
            {
                continue;
            }

            try
            {
                await using var stream = pack.Open(path);
                var definition = await JsonSerializer
                                       .DeserializeAsync<Component>(stream, FileHelper.SerializerOptions)
                                       .ConfigureAwait(false)
                              ?? throw new InvalidDataException("The definition is empty.");
                definitions.Add(entry.Uid, definition);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException($"Component definition '{path}' is not valid: {ex.Message}", ex);
            }
        }

        return definitions;
    }

    public static Result Convert(
        IProfilePackSource pack,
        MmcPack manifest,
        IReadOnlyDictionary<string, Component> definitions,
        string? instanceArguments = null)
    {
        var generated = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var files = new List<(string Source, string Target)>();
        var references = new List<PatchIndex.Entry>();

        // 本地 net.minecraft 定义取代原版产出（它本身就是 MMC 使用的完整原版声明）；加载器同理，
        //  禁用标准产出以免与本地定义的版本叠加。两者独立，只声明归档真正提供了定义的那一侧。
        var minecraft = definitions.GetValueOrDefault(MultiMcHelper.UID_MINECRAFT);
        var baseOperations = new List<PatchDocument.Operation>();
        if (minecraft is not null)
        {
            baseOperations.Add(Operation("vanilla", "replace", new PatchArtifact
            {
                MainClass = "net.minecraft.client.main.Main",
                CompatibleJavaMajors = minecraft.CompatibleJavaMajors is { Count: > 0 } declared ? [.. declared.Distinct().Order()] : [8],
                AssetIndex = ParseAssetIndex(minecraft)
            }));
        }

        if (definitions.Keys.Any(MultiMcHelper.UidToLoaderMappings.ContainsKey))
        {
            baseOperations.Add(Operation("loader.enabled", "replace", false));
        }

        if (baseOperations.Count > 0)
        {
            Add("_trident_base", new() { Name = "Imported launch defaults", Operations = baseOperations }, true);
        }

        var needsIntermediary = definitions.ContainsKey(PrismLauncherService.UID_FABRIC)
                             || definitions.ContainsKey(PrismLauncherService.UID_QUILT);
        foreach (var entry in manifest.Components.Where(x => !x.Disabled))
        {
            if (!definitions.TryGetValue(entry.Uid, out var definition))
            {
                if (needsIntermediary && entry.Uid == PrismLauncherService.UID_INTERMEDIARY)
                {
                    var version = entry.Version ?? entry.CachedVersion
                               ?? throw new InvalidDataException("The intermediary component has no version.");
                    var identity = PatchHelper.ParseIdentity($"net.fabricmc:intermediary:{version}");
                    Add(entry.Uid, new()
                    {
                        Name = "Intermediary mappings",
                        Operations =
                        [
                            Operation("launch.libraries", "append", new PatchLibrary[]
                            {
                                new() { Identity = PatchHelper.Identity(identity), Url = new(new Uri("https://maven.fabricmc.net/"), MavenPath(identity)) }
                            })
                        ]
                    }, true);
                }
                continue;
            }

            var path = $"patches/{entry.Uid}.json";
            List<PatchDocument.Operation> operations;
            try
            {
                operations = ConvertComponent(definition, entry.Uid, files, pack);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or NotSupportedException))
            {
                throw new InvalidDataException($"Component definition '{path}' cannot be converted: {ex.Message}", ex);
            }

            Add(entry.Uid,
                new()
                {
                    Name = definition.Name ?? entry.Uid,
                    Description = "Converted instance launch declarations",
                    Operations = operations
                },
                true);
        }

        if (!string.IsNullOrWhiteSpace(instanceArguments))
        {
            Add("_trident_arguments",
                new()
                {
                    Name = "Imported instance arguments",
                    Operations =
                    [
                        Operation("launch.jvmArguments", "append",
                                  ArgumentHelper.GroupArguments(ArgumentHelper.Tokenize(instanceArguments)))
                    ]
                },
                true);
        }

        generated.Add(PatchStorageHelper.INDEX_FILE_NAME,
                      JsonSerializer.SerializeToUtf8Bytes(new PackPatchIndex { Import = references },
                                                          FileHelper.SerializerOptions));
        return new(files.Distinct().ToArray(), generated);

        void Add(string id, PatchDocument patch, bool enabled)
        {
            ValidateUid(id);
            var path = $"{id}/patch.json";
            references.Add(new(path, enabled));
            generated.Add($"import/{path}", JsonSerializer.SerializeToUtf8Bytes(patch, FileHelper.SerializerOptions));
        }
    }

    private static List<PatchDocument.Operation> ConvertComponent(
        Component definition,
        string owner,
        List<(string Source, string Target)> files,
        IProfilePackSource pack)
    {
        foreach (var field in JAR_MODIFICATION_FIELDS)
        {
            if (definition.ExtraMembers?.ContainsKey(field) == true)
            {
                throw new NotSupportedException($"Component '{owner}' declares '{field}', which modifies the client JAR; Trident cannot reproduce it.");
            }
        }

        var operations = new List<PatchDocument.Operation>();
        foreach (var agent in definition.Agents ?? [])
        {
            operations.Add(Operation("launch.agents", "append", new[] { ParseAgent(agent, owner, files, pack) }));
        }

        if (!string.IsNullOrWhiteSpace(definition.MainClass))
        {
            operations.Add(Operation("launch.mainClass", "replace", definition.MainClass));
        }

        if (!string.IsNullOrWhiteSpace(definition.MinecraftArguments))
        {
            operations.Add(Operation("launch.gameArguments", "replace",
                                     ArgumentHelper.GroupArguments(ArgumentHelper.Tokenize(definition.MinecraftArguments))));
        }

        if (definition.CompatibleJavaMajors is { Count: > 0 } majors)
        {
            operations.Add(Operation("launch.compatibleJavaMajors", "intersect", majors));
        }

        if (definition.MainClass == "io.github.zekerzhayard.forgewrapper.installer.Main")
        {
            operations.Add(Operation("launch.jvmArguments", "append", ArgumentHelper.ForgeWrapperJvmArguments()));
        }

        var jvmArguments = (definition.JvmArguments ?? [])
                          .SelectMany(ArgumentHelper.Tokenize)
                          .ToArray();
        if (jvmArguments.Length > 0)
        {
            operations.Add(Operation("launch.jvmArguments", "append", ArgumentHelper.GroupArguments(jvmArguments)));
        }

        foreach (var tweaker in (definition.Tweakers ?? []).Distinct())
        {
            operations.Add(new()
            {
                Target = "launch.gameArguments", Action = "remove", Match = new() { Arguments = ["--tweakClass", tweaker] }
            });
            operations.Add(Operation("launch.gameArguments", "append", new[] { new[] { "--tweakClass", tweaker } }));
        }

        foreach (var trait in definition.Traits ?? [])
        {
            // NOTE: 只有首线程要求需要翻译；其余 trait 是其他启动器的行为提示，忽略即可，不影响本管线。
            if (trait != "FirstThreadOnMacOS")
            {
                continue;
            }

            operations.Add(new()
            {
                Target = "launch.jvmArguments", Action = "remove", Match = new() { Arguments = ["-XstartOnFirstThread"] },
                Rules = [new("allow", "osx")]
            });
            operations.Add(Operation("launch.jvmArguments", "append", new[] { new[] { "-XstartOnFirstThread" } }) with
            {
                Rules = [new("allow", "osx")]
            });
        }

        foreach (var library in (definition.Libraries ?? []).Concat(definition.ExtraLibraries ?? []))
        {
            AddLibraries(library, owner, operations, files, pack, false);
        }

        foreach (var library in definition.MavenFiles ?? [])
        {
            AddLibraries(library, owner, operations, files, pack, true);
        }

        if (definition.MainJar is { } mainJar)
        {
            var parsed = ParseLibraries(mainJar, owner, files, pack, false);
            operations.Add(Operation("launch.mainJar", "replace",
                                     parsed.FirstOrDefault(x => !x.Native)
                                        ?? throw new InvalidDataException("The imported main JAR declaration is invalid.")));
        }
        else if (owner == MultiMcHelper.UID_MINECRAFT && definition.Downloads is { } downloads && downloads.TryGetValue("client", out var client))
        {
            operations.Add(Operation("launch.mainJar", "replace", new PatchLibrary
            {
                Identity = $"com.mojang:minecraft:{definition.Version}:client",
                Url = client.Url ?? throw new InvalidDataException("The imported client download has no URL."),
                Hash = FileHash.FromSha1(client.Sha1)
            }));
        }

        if (owner == MultiMcHelper.UID_MINECRAFT)
        {
            operations.Add(Operation("launch.assetIndex", "replace", ParseAssetIndex(definition)));
        }

        return operations;
    }

    private static void AddLibraries(
        Component.Library source,
        string owner,
        List<PatchDocument.Operation> operations,
        List<(string Source, string Target)> files,
        IProfilePackSource pack,
        bool auxiliary)
    {
        foreach (var library in ParseLibraries(source, owner, files, pack, auxiliary))
        {
            operations.Add(Operation("launch.libraries", "append", new[] { library with { Rules = [] } }) with
            {
                Rules = library.Rules
            });
        }
    }

    private static PatchAgent ParseAgent(
        Component.Library source,
        string owner,
        List<(string Source, string Target)> files,
        IProfilePackSource pack) =>
        new()
        {
            Library = ParseLibraries(source, owner, files, pack, false).FirstOrDefault(x => !x.Native)
                   ?? throw new InvalidDataException("The imported agent declaration is invalid."),
            Arguments = source.Argument
        };

    private static IReadOnlyList<PatchLibrary> ParseLibraries(
        Component.Library source,
        string owner,
        List<(string Source, string Target)> files,
        IProfilePackSource pack,
        bool auxiliary)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new InvalidDataException("A library declaration has no name.");
        }

        var identity = PatchHelper.ParseIdentity(source.Name);
        var rules = ParseRules(source.Rules);
        var exclude = source.Extract?.Exclude ?? [];

        switch (source.Hint)
        {
            case null:
                break;

            case "local":
                {
                    var mavenPath = MavenPath(identity);
                    var file = new[] { $"libraries/{source.FileName ?? Path.GetFileName(mavenPath)}", $"libraries/{mavenPath}" }
                              .FirstOrDefault(x => pack.LengthOf(x) is not null)
                            ?? throw new FileNotFoundException($"Local library '{source.Name}' is missing from the instance archive.");
                    var local = $"assets/{mavenPath}";
                    files.Add((file, $"import/{owner}/{local}"));
                    return [new() { Identity = source.Name, Local = local, Classpath = !auxiliary, Rules = rules }];
                }

            case "always-stale":
                throw new NotSupportedException("Mutable always-stale library sources cannot be imported as locked patch assets.");

            default:
                throw new NotSupportedException($"Unsupported library hint '{source.Hint}'.");
        }

        var result = new List<PatchLibrary>();
        var hasDownloads = source.Downloads is not null;
        if (source.Downloads?.Artifact is { } artifact)
        {
            result.Add(Download(source.Name, artifact, false, !auxiliary, rules, exclude));
        }
        else if ((source.AbsoluteUrl ?? source.MisspelledAbsoluteUrl) is { } absolute)
        {
            result.Add(new() { Identity = source.Name, Url = absolute, Classpath = !auxiliary, Rules = rules });
        }
        else if (!hasDownloads || source.Natives is null)
        {
            var root = source.Url ?? new Uri("https://libraries.minecraft.net/");
            result.Add(new()
            {
                Identity = source.Name,
                Url = new(new Uri(root.AbsoluteUri.TrimEnd('/') + "/"), MavenPath(identity)),
                Classpath = !auxiliary,
                Rules = rules
            });
        }

        if (source.Natives is { } natives && source.Downloads?.Classifiers is { } classifiers)
        {
            foreach (var (os, classifier) in new[] { ("windows", natives.Windows), ("linux", natives.Linux), ("osx", natives.Osx) })
            {
                if (classifier is null)
                {
                    continue;
                }

                foreach (var arch in classifier.Contains("${arch}") ? new[] { "x86", "x64" } : new string?[] { null })
                {
                    var resolved = classifier.Replace("${arch}", arch == "x86" ? "32" : "64");
                    if (!classifiers.TryGetValue(resolved, out var download))
                    {
                        continue;
                    }

                    // Scope native availability with a leading allow, then retain exclusions from the source rules.
                    result.Add(Download(PatchHelper.Identity(identity with { Platform = resolved }),
                                        download,
                                        true,
                                        false,
                                        RestrictRules(rules, os, arch),
                                        exclude));
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<PatchDocument.PlatformRule> RestrictRules(
        IReadOnlyList<PatchDocument.PlatformRule> rules,
        string os,
        string? arch)
    {
        if (rules.Count == 0)
        {
            return [new("allow", os, arch is null ? null : "^" + arch + "$")];
        }

        var restricted = new List<PatchDocument.PlatformRule>();
        foreach (var rule in rules)
        {
            if (rule.Os is null || rule.Os == os || rule.Os.StartsWith(os + "-", StringComparison.Ordinal))
            {
                restricted.Add(rule with { Os = rule.Os ?? os });
            }
        }

        if (arch is not null)
        {
            restricted.Add(new("disallow", os, arch == "x86" ? "^(?!x86$).*" : "^(?!x64$).*"));
        }

        return restricted.Count == 0 ? [new("disallow")] : restricted;
    }

    private static PatchLibrary Download(
        string name,
        Component.Library.DownloadsEntry.ArtifactEntry download,
        bool native,
        bool classpath,
        IReadOnlyList<PatchDocument.PlatformRule> rules,
        IReadOnlyList<string> exclude) =>
        new()
        {
            Identity = name,
            Url = download.Url ?? throw new InvalidDataException($"Library '{name}' has no download URL."),
            Hash = FileHash.FromSha1(download.Sha1),
            Native = native,
            Classpath = classpath,
            Rules = rules,
            Exclude = exclude
        };

    private static IReadOnlyList<PatchDocument.PlatformRule> ParseRules(IReadOnlyList<Component.Library.Rule>? rules)
    {
        var converted = (rules ?? []).Select(rule =>
        {
            if (rule.ExtraMembers?.Count > 0)
            {
                throw new NotSupportedException("The imported library rule contains unsupported conditions.");
            }

            var os = rule.Os;
            if (os is not null && os.Keys.Any(x => x is not ("name" or "arch" or "version")))
            {
                throw new NotSupportedException("The imported library rule contains an unsupported platform condition.");
            }

            var arch = os?.GetValueOrDefault("arch");
            return new PatchDocument.PlatformRule(rule.Action ?? throw new InvalidDataException("A library rule has no action."),
                                                  os?.GetValueOrDefault("name"),
                                                  arch switch
                                                  {
                                                      "amd64" or "x86_64" => "^x64$",
                                                      "aarch64" => "^arm64$",
                                                      "x86" => "^x86$",
                                                      _ => arch
                                                  },
                                                  os?.GetValueOrDefault("version"));
        }).ToList();
        return converted;
    }

    private static LockData.AssetData ParseAssetIndex(Component definition)
    {
        if (definition.AssetIndex is not { } index
         || string.IsNullOrWhiteSpace(index.Id)
         || index.Url is null)
        {
            throw new InvalidDataException("The imported Minecraft definition has no asset index.");
        }

        return new(index.Id, index.Url, FileHash.FromSha1(index.Sha1));
    }

    private static string MavenPath(LockData.Library.Identity id) =>
        $"{id.Namespace.Replace('.', '/')}/{id.Name}/{id.Version}/{id.Name}-{id.Version}{(id.Platform is null ? "" : "-" + id.Platform)}.{id.Extension}";

    private static PatchDocument.Operation Operation<T>(string target, string action, T value) =>
        new() { Target = target, Action = action, Value = JsonSerializer.SerializeToElement(value, FileHelper.SerializerOptions) };

    private static void ValidateUid(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            throw new InvalidDataException($"Invalid metadata identifier '{value}'.");
        }
    }
}
