using System.Text.Json;
using System.Text.Json.Nodes;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.PrismLauncherApi;

namespace TridentCore.Core.Utilities;

public static class MetadataExportHelper
{
    public static JsonObject Convert(LaunchComponent component, IReadOnlyList<LaunchArgument> gameArguments,
                                     PackedProfileContainer container)
    {
        LaunchDefinitionHelper.Validate(component, component.Id);
        if (component.JavaArguments.Replace is not null || component.StartOnFirstThread == false)
        {
            throw new NotSupportedException($"MMC cannot represent the JVM replacement policy of component '{component.Id}'");
        }
        var patch = new JsonObject
        {
            ["formatVersion"] = 1, ["uid"] = component.Id, ["name"] = component.Id,
            ["version"] = component.Version
        };
        if (component.MainClass is not null) patch["mainClass"] = component.MainClass;
        if (component.JavaMajors is not null) patch["compatibleJavaMajors"] = JsonSerializer.SerializeToNode(component.JavaMajors);
        if (component.StartOnFirstThread == true) patch["+traits"] = new JsonArray("FirstThreadOnMacOS");
        if (component.Requires.Count > 0) patch["requires"] = Requirements(component.Requires);
        if (component.Conflicts.Count > 0) patch["conflicts"] = Requirements(component.Conflicts);
        if (component.AssetIndex is { } index)
        {
            if (index.Url.IsFile) throw new NotSupportedException("MMC export cannot carry a local asset index");
            if (index.Hash is not null && index.Hash.Algorithm != HashAlgorithm.Sha1)
                throw new NotSupportedException("MMC export cannot preserve the asset index checksum");
            patch["assetIndex"] = new JsonObject
            {
                ["id"] = index.Id, ["url"] = index.Url.ToString(), ["sha1"] = Sha1(index.Hash)
            };
        }
        if (component.GameArguments.Replace is not null || component.GameArguments.Append.Count > 0)
        {
            if (gameArguments.All(x => x.Rules is null || x.Rules.Count == 0))
            {
                patch["minecraftArguments"] = string.Join(' ', gameArguments.Select(x => Quote(x.Value)));
            }
            else
            {
                patch["arguments"] = new JsonObject { ["game"] = Arguments(gameArguments) };
            }
        }
        if (component.JavaArguments.Append.Count > 0)
        {
            if (component.JavaArguments.Append.Any(x => x.Value.Contains("${artifact:") || x.Value.Contains("${minecraft_jar}")))
            {
                throw new NotSupportedException($"MMC cannot represent native artifact arguments in component '{component.Id}'");
            }
            if (component.JavaArguments.Append.All(x => x.Rules is null || x.Rules.Count == 0))
            {
                patch["+jvmArgs"] = new JsonArray(component.JavaArguments.Append.Select(x => JsonValue.Create(x.Value)).ToArray());
            }
            else
            {
                var arguments = patch["arguments"] as JsonObject ?? new JsonObject();
                arguments["jvm"] = Arguments(component.JavaArguments.Append);
                if (patch["arguments"] is null) patch["arguments"] = arguments;
            }
        }

        var libraries = new JsonArray();
        var maven = new JsonArray();
        var agents = new JsonArray();
        foreach (var binding in component.Libraries.Where(x => x.Use != LaunchLibrary.Usage.Native))
        {
            var library = Artifact(binding.Artifact, container);
            if (binding.Platform is not null)
            {
                throw new NotSupportedException("MMC export requires platform conditions to be expressed as library rules");
            }
            if (binding.Rules.Count > 0) library["rules"] = Rules(binding.Rules);
            switch (binding.Use)
            {
                case LaunchLibrary.Usage.Classpath: libraries.Add(library); break;
                case LaunchLibrary.Usage.Required: maven.Add(library); break;
                case LaunchLibrary.Usage.Client:
                    if (patch["mainJar"] is not null) throw new NotSupportedException("MMC supports only one client artifact per component");
                    patch["mainJar"] = library;
                    break;
                case LaunchLibrary.Usage.Agent:
                    if (binding.AgentArguments is not null) throw new NotSupportedException("MMC export cannot represent Java agent options");
                    agents.Add(library);
                    break;
            }
        }

        var nativeGroups = new List<List<LaunchLibrary>>();
        foreach (var binding in component.Libraries.Where(x => x.Use == LaunchLibrary.Usage.Native))
        {
            var group = nativeGroups.LastOrDefault();
            if (group is null || NativeKey(group[0]) != NativeKey(binding)
             || group.Any(x => x.Platform == binding.Platform))
            {
                nativeGroups.Add([binding]);
            }
            else
            {
                group.Add(binding);
            }
        }
        foreach (var variants in nativeGroups)
        {
            if (variants.Any(x => x.Platform is null || x.Artifact.Id.Classifier is null || x.Artifact.Url.IsFile))
            {
                throw new NotSupportedException("MMC native export requires remote classified artifacts with explicit platform targets");
            }
            var classifiers = new JsonObject();
            foreach (var variant in variants)
            {
                var classifier = variant.Artifact.Id.Classifier!;
                var value = Download(variant.Artifact);
                if (classifiers[classifier] is { } existing && !JsonNode.DeepEquals(existing, value))
                {
                    throw new NotSupportedException($"MMC classifier '{classifier}' has different files for different targets");
                }
                classifiers[classifier] = value;
            }
            var mappings = new JsonObject();
            foreach (var os in variants.GroupBy(x => x.Platform!.Os))
            {
                var architectures = os.ToDictionary(x => x.Platform!.Architecture, x => x.Artifact.Id.Classifier!);
                var fallback = architectures.GetValueOrDefault("x64") ?? architectures.Values.First();
                if (architectures.TryGetValue("x86", out var x86) && x86.Replace("32", "64") == fallback && x86 != fallback)
                {
                    mappings[os.Key] = x86.Replace("32", "${arch}");
                }
                else
                {
                    mappings[os.Key] = fallback;
                }
                foreach (var (architecture, classifier) in architectures)
                {
                    if (classifier == fallback || (architecture == "x86" && mappings[os.Key]!.GetValue<string>().Contains("${arch}"))) continue;
                    mappings[$"{os.Key}-{(architecture == "arm" ? "arm32" : architecture)}"] = classifier;
                }
            }
            var native = new JsonObject
            {
                ["name"] = ArtifactHelper.CoordinateOf(NativeKey(variants[0]).Identity),
                ["downloads"] = new JsonObject { ["classifiers"] = classifiers },
                ["natives"] = mappings
            };
            if (variants[0].Rules.Count > 0) native["rules"] = Rules(variants[0].Rules);
            if (variants[0].ExtractExcludes.Count > 0)
                native["extract"] = new JsonObject { ["exclude"] = JsonSerializer.SerializeToNode(variants[0].ExtractExcludes) };
            libraries.Add(native);
        }
        if (libraries.Count > 0) patch["libraries"] = libraries;
        if (maven.Count > 0) patch["mavenFiles"] = maven;
        if (agents.Count > 0) patch["+agents"] = agents;
        if (component.Libraries.Any(x => x.Use == LaunchLibrary.Usage.Native))
        {
            var reconstructed = MetadataComponentHelper.Convert(patch.Deserialize<Component>(JsonSerializerOptions.Web)!,
                component.Id, component.Version, "unused").Component;
            string NativeShape(LaunchComponent value) => HashHelper.ComputeObjectHash(value.Libraries
                .Where(x => x.Use == LaunchLibrary.Usage.Native)
                .GroupBy(x => x.Platform!)
                .OrderBy(x => x.Key.Os, StringComparer.Ordinal).ThenBy(x => x.Key.Architecture, StringComparer.Ordinal)
                .Select(x => new { Platform = x.Key, Bindings = x.ToArray() }).ToArray());
            if (NativeShape(component) != NativeShape(reconstructed))
            {
                throw new NotSupportedException($"MMC cannot preserve the native platform selection of component '{component.Id}'");
            }
        }
        return patch;
    }

    private static (LaunchArtifact.Identity Identity, string Policy) NativeKey(LaunchLibrary binding) =>
        (binding.Artifact.Id with { Classifier = null }, HashHelper.ComputeObjectHash(new { binding.Rules, binding.ExtractExcludes }));

    private static JsonObject Artifact(LaunchArtifact artifact, PackedProfileContainer container)
    {
        var result = new JsonObject { ["name"] = ArtifactHelper.CoordinateOf(artifact.Id) };
        if (artifact.Url.IsFile)
        {
            var name = ArtifactHelper.FileNameOf(artifact.Id);
            var target = $"libraries/{name}";
            if (container.Files.TryGetValue(target, out var previous) && !FileHelper.IsPathEquivalent(previous, artifact.Url.LocalPath)
             && FileHelper.ComputeHash(previous, HashAlgorithm.Sha256) != FileHelper.ComputeHash(artifact.Url.LocalPath, HashAlgorithm.Sha256))
            {
                throw new FormatException($"MMC export has conflicting library filenames '{name}'");
            }
            container.Files[target] = artifact.Url.LocalPath;
            result["MMC-hint"] = "local";
        }
        else
        {
            result["downloads"] = new JsonObject { ["artifact"] = Download(artifact) };
        }
        return result;
    }

    private static JsonObject Download(LaunchArtifact artifact)
    {
        if (artifact.Hash is not null && artifact.Hash.Algorithm != HashAlgorithm.Sha1)
        {
            throw new NotSupportedException($"MMC cannot preserve the checksum of '{ArtifactHelper.CoordinateOf(artifact.Id)}'");
        }
        return new() { ["url"] = artifact.Url.ToString(), ["sha1"] = Sha1(artifact.Hash) };
    }

    private static string? Sha1(FileHash? hash) => hash?.Algorithm == HashAlgorithm.Sha1 ? hash.Value : null;

    private static JsonArray Requirements(IEnumerable<LaunchRequirement> requirements) => new(requirements.Select(x =>
        new JsonObject { ["uid"] = x.Id, ["equals"] = x.Version, ["suggests"] = x.SuggestedVersion }).ToArray<JsonNode>());

    private static JsonArray Arguments(IEnumerable<LaunchArgument> arguments) => new(arguments.Select(x =>
        x.Rules is null || x.Rules.Count == 0 ? (JsonNode)JsonValue.Create(x.Value)!
            : new JsonObject { ["value"] = x.Value, ["rules"] = Rules(x.Rules) }).ToArray());

    private static JsonArray Rules(IEnumerable<LaunchRule> rules) => new(rules.Select(x =>
    {
        var result = new JsonObject { ["action"] = x.Allow ? "allow" : "disallow" };
        if (x.Os is not null || x.Architecture is not null || x.OsVersion is not null)
        {
            var os = new JsonObject();
            if (x.Os is not null) os["name"] = x.Os;
            if (x.Architecture is not null) os["arch"] = x.Architecture == "x64" ? "x86_64" : x.Architecture;
            if (x.OsVersion is not null) os["version"] = x.OsVersion;
            result["os"] = os;
        }
        return result;
    }).ToArray<JsonNode>());

    private static string Quote(string value) => value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
        ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;
}
