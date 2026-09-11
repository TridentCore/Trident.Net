using System.Text.Json;
using System.Text.RegularExpressions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying;

public sealed class PatchSet(IReadOnlyList<PatchSet.Instruction> instructions)
{
    private const string JAVA_MAJORS = "compatibleJavaMajors";
    private static readonly HashSet<string> FIELDS = ["libraries", "agents", "gameArguments", "jvmArguments", "mainClass", JAVA_MAJORS, "mainJar", "assetIndex"];

    public record LocalAsset(string Path, FileHash? Hash);
    public record Instruction(string Source, PatchDocument.Operation Operation, IReadOnlyDictionary<string, LocalAsset> Assets);

    public string Fingerprint(string scope) => PatchHelper.Fingerprint(instructions
        .Where(x => Scope(x.Operation.Target) == scope)
        .Select(x => new { x.Source, x.Operation, x.Assets }));

    public bool HasReplacement(string target) => instructions.Any(x => x.Operation.Target == target && x.Operation.Action == "replace" && x.Operation.Match is null);

    public bool IsLoaderEnabled()
    {
        var enabled = true;
        foreach (var instruction in instructions.Where(x => x.Operation.Target == "loader.enabled"))
        {
            enabled = Read<bool>(instruction.Operation);
        }
        return enabled;
    }

    public LockData.ArtifactData Apply(string scope, LockData.ArtifactData? input)
    {
        var relevant = instructions.Where(x => Scope(x.Operation.Target) == scope && x.Operation.Target != "loader.enabled").ToList();
        var start = relevant.FindLastIndex(x => x.Operation.Target == scope);
        if (start < 0)
        {
            start = 0;
        }
        var result = input is null ? null : LibraryHelper.Resolve(input);
        for (var i = start; i < relevant.Count; i++)
        {
            var instruction = relevant[i];
            try
            {
                result = LibraryHelper.Resolve(ApplyOne(result, instruction));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException($"Patch '{instruction.Source}', operation {i + 1} ({instruction.Operation.Target}): {ex.Message}", ex);
            }
        }
        return result ?? throw new InvalidDataException($"No data supplied for {scope}.");
    }

    public static void Validate(PatchDocument.Operation operation)
    {
        var scope = Scope(operation.Target);
        var field = operation.Target.Contains('.') ? operation.Target[(operation.Target.IndexOf('.') + 1)..] : null;
        if (scope is not ("vanilla" or "loader" or "launch")
         || (field is not null && !FIELDS.Contains(field) && operation.Target != "loader.enabled"))
        {
            throw new InvalidDataException($"Unknown patch target '{operation.Target}'.");
        }
        if (operation.Action is not ("replace" or "append" or "remove" or "intersect"))
        {
            throw new InvalidDataException($"Unknown patch operation '{operation.Action}'.");
        }
        if (operation.Action == "remove" && operation.Value is { ValueKind: not JsonValueKind.Null })
        {
            throw new InvalidDataException("Remove uses a selector, not a replacement value.");
        }
        if (operation.Action != "remove" && operation.Value is not { ValueKind: not JsonValueKind.Null })
        {
            throw new InvalidDataException($"{operation.Target}: {operation.Action} requires a value.");
        }
        if (field is null && operation.Action != "replace")
        {
            throw new InvalidDataException("A complete stage result can only be replaced.");
        }
        var isDependency = field is "libraries" or "agents";
        var isArguments = field is "gameArguments" or "jvmArguments";
        var isJavaMajors = field == JAVA_MAJORS;
        // NOTE: 交集的语义只对 Java 兼容版本集合成立；其余字段的“交”没有定义，尤其集合类字段的
        //  append/remove 与运行时的 major 约束毫无关系。
        if (operation.Action == "intersect" && !isJavaMajors)
        {
            throw new InvalidDataException($"{operation.Target} does not support intersect.");
        }
        if (field is not null && !isDependency && !isArguments
         && operation.Action != "replace" && !(isJavaMajors && operation.Action == "intersect")
         && !(field == "mainJar" && operation.Action == "remove"))
        {
            throw new InvalidDataException($"{operation.Target} does not support {operation.Action}.");
        }
        if (operation.Match is { } selector)
        {
            if (operation.Action == "append")
            {
                throw new InvalidDataException("Append does not accept a selector.");
            }
            ValidateSelector(field, selector);
        }
        if (operation.Before is not null && operation.After is not null)
        {
            throw new InvalidDataException("Specify either before or after, not both.");
        }
        if ((operation.Before is not null || operation.After is not null) && (!isDependency || operation.Action != "append"))
        {
            throw new InvalidDataException("Insertion positions require a library or agent append.");
        }
        if ((operation.IfMissing is not null || operation.OnlyIfNewer)
         && (!isDependency || operation.Action != "replace" || operation.Match is null))
        {
            throw new InvalidDataException("Conditional replacement requires a library or agent selector.");
        }
        if (operation.IfMissing is not (null or "append" or "ignore"))
        {
            throw new InvalidDataException("ifMissing must be append or ignore.");
        }
        if (operation.Rules is null)
        {
            throw new InvalidDataException("Platform rules cannot be null.");
        }
        _ = PatchHelper.Matches(operation.Rules);
        if (operation.Action != "remove")
        {
            ValidateValue(operation, field);
        }
        foreach (var library in GetLibraries(operation))
        {
            _ = PatchHelper.ParseIdentity(library.Identity);
            if ((library.Url is null) == (library.Local is null))
            {
                throw new InvalidDataException($"Library '{library.Identity}' requires exactly one of url or local.");
            }
            if (library.Url is not null && (!library.Url.IsAbsoluteUri || library.Url.Scheme is not ("https" or "http")))
            {
                throw new InvalidDataException("A library URL must use HTTP or HTTPS.");
            }
            _ = PatchHelper.Matches(library.Rules);
        }
        foreach (var agent in GetAgents(operation))
        {
            if (agent.Library.Native)
            {
                throw new InvalidDataException($"Agent '{agent.Library.Identity}' cannot extract natives.");
            }
        }
    }

    public static IEnumerable<PatchLibrary> GetLibraries(PatchDocument.Operation operation)
    {
        if (operation.Action == "remove")
        {
            return [];
        }
        if (!operation.Target.Contains('.'))
        {
            var artifact = Read<PatchArtifact>(operation);
            var libraries = artifact.Libraries.Concat(artifact.Agents.Select(x => x.Library));
            return artifact.MainJar is { } main ? libraries.Append(main) : libraries;
        }
        if (operation.Target.EndsWith(".libraries", StringComparison.Ordinal))
        {
            return Read<IReadOnlyList<PatchLibrary>>(operation);
        }
        if (operation.Target.EndsWith(".agents", StringComparison.Ordinal))
        {
            return GetAgents(operation).Select(x => x.Library);
        }
        return operation.Target.EndsWith(".mainJar", StringComparison.Ordinal) ? [Read<PatchLibrary>(operation)] : [];
    }

    private static IEnumerable<PatchAgent> GetAgents(PatchDocument.Operation operation)
    {
        if (operation.Action == "remove")
        {
            return [];
        }
        if (!operation.Target.Contains('.'))
        {
            return Read<PatchArtifact>(operation).Agents;
        }
        return operation.Target.EndsWith(".agents", StringComparison.Ordinal)
                   ? Read<IReadOnlyList<PatchAgent>>(operation)
                   : [];
    }

    public static void ValidateArtifact(LockData.ArtifactData artifact)
    {
        // NOTE: 集合允许为空——交集本就能把需求缩到空，那由仲裁点报 NoCompatibleJava；在这里报会把
        //  它误归因成 patch 结构错误。空只作为运算结果出现，文件里写出的值一律要求非空。
        if (string.IsNullOrWhiteSpace(artifact.MainClass) || artifact.CompatibleJavaMajors is null
         || artifact.AssetIndex is null || string.IsNullOrWhiteSpace(artifact.AssetIndex.Id))
        {
            throw new InvalidDataException("Launch data must provide a main class, Java major and asset index.");
        }
        _ = ValidateArguments(artifact.GameArguments);
        _ = ValidateArguments(artifact.JavaArguments);
    }

    private static LockData.ArtifactData ApplyOne(LockData.ArtifactData? artifact, Instruction instruction)
    {
        var operation = instruction.Operation;
        if (!operation.Target.Contains('.'))
        {
            var replacement = Read<PatchArtifact>(operation);
            return new(replacement.MainClass, Normalize(replacement.CompatibleJavaMajors),
                       ValidateArguments(replacement.GameArguments),
                       [.. replacement.DefaultJvmArguments ? ArgumentHelper.DefaultJvmArguments() : [], .. ValidateArguments(replacement.JvmArguments)],
                       ConvertLibraries(replacement.Libraries, instruction), replacement.AssetIndex)
            {
                MainJar = replacement.MainJar is null || !PatchHelper.Matches(replacement.MainJar.Rules)
                              ? null : ConvertLibrary(replacement.MainJar, instruction),
                Agents = ConvertAgents(replacement.Agents, instruction)
            };
        }
        if (artifact is null)
        {
            throw new InvalidDataException("A partial operation requires an existing stage result.");
        }
        var field = operation.Target[(operation.Target.IndexOf('.') + 1)..];
        if (field == "mainJar" && operation.Action != "remove" && !PatchHelper.Matches(Read<PatchLibrary>(operation).Rules))
        {
            return artifact;
        }
        return field switch
        {
            "mainClass" => artifact with { MainClass = Read<string>(operation) },
            JAVA_MAJORS => artifact with
            {
                CompatibleJavaMajors = operation.Action == "intersect"
                                           ? Intersect(artifact.CompatibleJavaMajors, Read<IReadOnlyList<uint>>(operation))
                                           : Normalize(Read<IReadOnlyList<uint>>(operation))
            },
            "assetIndex" => artifact with { AssetIndex = Read<LockData.AssetData>(operation) },
            "mainJar" => artifact with { MainJar = operation.Action == "remove" ? null : ConvertLibrary(Read<PatchLibrary>(operation), instruction) },
            "libraries" => artifact with { Libraries = ApplyLibraries(artifact.Libraries, instruction) },
            "agents" => artifact with { Agents = ApplyAgents(artifact.Agents, instruction) },
            "gameArguments" => artifact with { GameArguments = ApplyArguments(artifact.GameArguments, operation) },
            "jvmArguments" => artifact with { JavaArguments = ApplyArguments(artifact.JavaArguments, operation) },
            _ => throw new InvalidDataException($"Unknown target '{operation.Target}'.")
        };
    }

    private static IReadOnlyList<LockData.Library> ApplyLibraries(IReadOnlyList<LockData.Library> original, Instruction instruction) =>
        ApplyDependencies(original,
            instruction.Operation.Action == "remove" ? [] : ConvertLibraries(Read<IReadOnlyList<PatchLibrary>>(instruction.Operation), instruction),
            instruction.Operation, x => x, LibraryHelper.Insert);

    private static IReadOnlyList<LockData.Agent> ApplyAgents(IReadOnlyList<LockData.Agent> original, Instruction instruction) =>
        ApplyDependencies(original,
            instruction.Operation.Action == "remove" ? [] : ConvertAgents(Read<IReadOnlyList<PatchAgent>>(instruction.Operation), instruction),
            instruction.Operation, x => x.Library, LibraryHelper.InsertAgents);

    private static IReadOnlyList<T> ApplyDependencies<T>(
        IReadOnlyList<T> original, IReadOnlyList<T> replacements, PatchDocument.Operation operation,
        Func<T, LockData.Library> library,
        Func<IReadOnlyList<T>, IReadOnlyList<T>, int, IReadOnlyList<T>> insert)
    {
        var result = original.ToList();
        if (operation.Action == "replace" && operation.Match is null)
        {
            return replacements;
        }
        if (operation.Action == "append")
        {
            var anchor = operation.Before ?? operation.After;
            var position = anchor is null ? result.Count : result.FindIndex(x => IdentityMatches(library(x).Id, anchor));
            if (position < 0)
            {
                throw new InvalidDataException($"Insertion anchor '{anchor}' does not exist in '{operation.Target}'.");
            }
            if (operation.After is not null)
            {
                position++;
            }
            return insert(result, replacements, position);
        }
        var indices = result.Select((value, index) => (value, index))
            .Where(x => LibraryMatches(library(x.value), operation.Match)).Select(x => x.index).ToArray();
        if (operation.Action == "replace" && indices.Length == 0)
        {
            if (operation.IfMissing == "append")
            {
                return insert(result, replacements, result.Count);
            }
            if (operation.IfMissing == "ignore")
            {
                return result;
            }
            throw new InvalidDataException($"Replacement selector matched nothing in '{operation.Target}'.");
        }
        if (operation.OnlyIfNewer)
        {
            if (indices.Length != 1 || replacements.Count != 1)
            {
                throw new InvalidDataException("Version comparison requires one selected entry and one replacement.");
            }
            if (PatchHelper.CompareVersions(library(replacements[0]).Id.Version, library(result[indices[0]]).Id.Version) <= 0)
            {
                return result;
            }
        }
        foreach (var index in indices.Reverse())
        {
            result.RemoveAt(index);
        }
        if (operation.Action == "replace")
        {
            return insert(result, replacements, indices[0]);
        }
        return result;
    }

    private static IReadOnlyList<string[]> ApplyArguments(IReadOnlyList<string[]> original, PatchDocument.Operation operation)
    {
        var result = original.Select(x => x.ToArray()).ToList();
        var values = operation.Action == "remove" ? [] : Read<IReadOnlyList<string[]>>(operation);
        _ = ValidateArguments(values);
        if (operation.Action == "append")
        {
            result.AddRange(values);
        }
        else if (operation.Match is null)
        {
            result = values.ToList();
        }
        else
        {
            var prefix = operation.Match.Arguments;
            if (prefix is null || prefix.Count == 0)
            {
                throw new InvalidDataException("Argument selectors require a nonempty arguments prefix.");
            }
            var indices = result.Select((value, index) => (value, index))
                .Where(x => x.value.Take(prefix.Count).SequenceEqual(prefix)).Select(x => x.index).ToArray();
            if (operation.Action == "replace" && indices.Length == 0)
            {
                throw new InvalidDataException("Argument replacement selector matched nothing.");
            }
            foreach (var index in indices.Reverse())
            {
                result.RemoveAt(index);
            }
            if (operation.Action == "replace")
            {
                result.InsertRange(indices[0], values);
            }
        }
        return ValidateArguments(result);
    }

    // 库与 agent 的载荷已由 GetLibraries/GetAgents 反序列化，其余目标的载荷在此校验，
    //  使文档在加载/保存时就可用，而不必等到部署期才暴露错形状。
    private static void ValidateValue(PatchDocument.Operation operation, string? field)
    {
        try
        {
            switch (field)
            {
                case null:
                    var artifact = Read<PatchArtifact>(operation);
                    if (string.IsNullOrWhiteSpace(artifact.MainClass)
                     || artifact.CompatibleJavaMajors is not { Count: > 0 } majors
                     || majors.Any(x => x == 0)
                     || artifact.AssetIndex is null
                     || string.IsNullOrWhiteSpace(artifact.AssetIndex.Id)
                     || artifact.AssetIndex.Url is null)
                    {
                        throw new InvalidDataException("A whole-result replacement needs a main class, Java majors and asset index.");
                    }
                    _ = ValidateArguments(artifact.GameArguments);
                    _ = ValidateArguments(artifact.JvmArguments);
                    break;

                case "mainClass":
                    if (string.IsNullOrWhiteSpace(Read<string>(operation)))
                    {
                        throw new InvalidDataException("mainClass must be a nonempty string.");
                    }
                    break;

                case JAVA_MAJORS:
                    if (Read<IReadOnlyList<uint>>(operation) is not { Count: > 0 } declared || declared.Any(x => x == 0))
                    {
                        throw new InvalidDataException("Compatible Java majors must be a non-empty list of positive numbers.");
                    }
                    break;

                case "assetIndex":
                    var assetIndex = Read<LockData.AssetData>(operation);
                    if (string.IsNullOrWhiteSpace(assetIndex.Id) || assetIndex.Url is null)
                    {
                        throw new InvalidDataException("assetIndex needs an id and a url.");
                    }
                    break;

                case "gameArguments" or "jvmArguments":
                    _ = ValidateArguments(Read<IReadOnlyList<string[]>>(operation));
                    break;
            }
        }
        catch (Exception ex) when (ex is not (InvalidDataException or OperationCanceledException))
        {
            throw new InvalidDataException($"{operation.Target}: {ex.Message}", ex);
        }
    }

    private static void ValidateSelector(string? field, PatchDocument.Selector selector)
    {
        var valid = field switch
        {
            "libraries" => selector.Arguments is null,
            "agents" => selector.Arguments is null && selector.Native is null && selector.Classpath is null,
            "gameArguments" or "jvmArguments" => selector.Arguments is { Count: > 0 }
                && selector.Identity is null && selector.Native is null && selector.Classpath is null,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException($"Invalid selector for '{field ?? "stage replacement"}'.");
        }
    }

    private static bool LibraryMatches(LockData.Library library, PatchDocument.Selector? selector) =>
        selector is null || ((selector.Identity is null || IdentityMatches(library.Id, selector.Identity))
                         && (selector.Native is null || selector.Native == library.IsNative)
                         && (selector.Classpath is null || selector.Classpath == library.IsPresent));

    private static bool IdentityMatches(LockData.Library.Identity id, string pattern)
    {
        if (pattern.Count(x => x == ':') == 1)
        {
            pattern += ":*";
        }
        return Regex.IsMatch(PatchHelper.Identity(id), "^" + Regex.Escape(pattern).Replace("\\*\\*", ".*").Replace("\\*", "[^:@]*") + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static IReadOnlyList<LockData.Agent> ConvertAgents(IEnumerable<PatchAgent> agents, Instruction instruction) =>
        agents.Where(x => PatchHelper.Matches(x.Library.Rules))
            .Select(x => new LockData.Agent(ConvertLibrary(x.Library, instruction) with { IsPresent = false }, x.Arguments))
            .ToArray();

    private static IReadOnlyList<LockData.Library> ConvertLibraries(IEnumerable<PatchLibrary> libraries, Instruction instruction) =>
        libraries.Where(x => PatchHelper.Matches(x.Rules)).Select(x => ConvertLibrary(x, instruction)).ToArray();

    private static LockData.Library ConvertLibrary(PatchLibrary library, Instruction instruction)
    {
        var local = library.Local is null ? null : instruction.Assets[library.Local];
        return new(PatchHelper.ParseIdentity(library.Identity), library.Url, library.Hash ?? local?.Hash, library.Native, library.Classpath)
        {
            LocalPath = local?.Path,
            CacheKey = local is null ? PatchHelper.Fingerprint(new { library.Url, library.Hash }) : null,
            Exclude = library.Exclude
        };
    }

    private static IReadOnlyList<string[]> ValidateArguments(IReadOnlyList<string[]> groups)
    {
        if (groups.Any(x => x is null || x.Length == 0 || x.Any(y => y is null)))
        {
            throw new InvalidDataException("Arguments must be nonempty arrays of tokens.");
        }
        return groups;
    }

    // 去重并升序：集合语义与顺序无关，但顺序决定指纹，必须先归一化再参与缓存与比较。
    private static IReadOnlyList<uint> Normalize(IEnumerable<uint>? majors) =>
        [.. (majors ?? []).Where(x => x != 0).Distinct().Order()];

    private static IReadOnlyList<uint> Intersect(IReadOnlyList<uint>? left, IEnumerable<uint>? right)
    {
        var rightNormalized = Normalize(right);
        return [.. Normalize(left).Where(rightNormalized.Contains)];
    }

    private static T Read<T>(PatchDocument.Operation operation)
    {
        if (operation.Value is { } value && value.Deserialize<T>(PatchHelper.JsonOptions) is { } result)
        {
            return result;
        }
        throw new InvalidDataException($"Invalid value for '{operation.Target}'.");
    }

    private static string Scope(string target) => target.Split('.')[0];
}
