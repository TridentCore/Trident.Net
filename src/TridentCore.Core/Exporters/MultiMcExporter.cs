using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Exporters;

public class MultiMcExporter(PrismLauncherService prismLauncherService, IServiceProvider serviceProvider)
    : IProfileExporter
{
    private static readonly JsonSerializerOptions SERIALIZER_OPTIONS =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    #region IProfileExporter Members

    public string Label => MultiMcHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key)
        {
            OverrideDirectoryName = MultiMcHelper.PACK_MINECRAFT_DIR
        };
        var setup = pack.Profile.Setup;
        var snapshot = LaunchPlanSnapshot.LoadOrNull(pack.Key);

        var mcComponent = await prismLauncherService
                               .GetVersionAsync(MultiMcHelper.UID_MINECRAFT, setup.Version, default)
                               .ConfigureAwait(false);
        var lwjglVersion = mcComponent.Requires.FirstOrDefault(r => r.Uid == MultiMcHelper.UID_LWJGL3)?.Suggest;
        var components = new List<MmcPack.ComponentEntry> { new(MultiMcHelper.UID_MINECRAFT, setup.Version) };
        if (lwjglVersion is not null)
        {
            components.Insert(0, new(MultiMcHelper.UID_LWJGL3, lwjglVersion));
        }

        if (!string.IsNullOrEmpty(setup.Loader) && LoaderHelper.TryParse(setup.Loader, out var loader)
         && MultiMcHelper.LoaderToUidMappings.TryGetValue(loader.Identity, out var loaderUid))
        {
            components.Add(new(loaderUid, loader.Version));
        }

        var componentUids = components.Select(x => x.Uid).ToHashSet(StringComparer.Ordinal);
        var patchUids = new HashSet<string>(StringComparer.Ordinal);
        var sourceOrderUids = new List<string>();
        if (snapshot is not null)
        {
            foreach (var entry in snapshot.Entries.Where(x => x.Origin == LaunchLayer.Origin.Managed))
            {
                var candidate = LaunchPlanFileHelper.SourceUid(Path.GetFileName(entry.Path));
                var uid = patchUids.Contains(candidate)
                              ? DisambiguateUid(candidate, entry.RelativePath, componentUids, patchUids)
                              : candidate;
                patchUids.Add(uid);
                sourceOrderUids.Add(uid);
                var patch = ConvertDocument(entry.RawOperations,
                                            uid,
                                            entry.Path,
                                            container);
                var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(patch, SERIALIZER_OPTIONS));
                container.Attachments.Add($"patches/{uid}.json", stream);
                if (componentUids.Add(uid))
                {
                    components.Add(new(uid, "1"));
                }
            }
        }

        var orderedComponents = components.Select(x => x.Uid)
                                           .Concat(sourceOrderUids)
                                           .Distinct(StringComparer.Ordinal)
                                           .Select(uid => components.FirstOrDefault(x => x.Uid == uid)
                                                        ?? new MmcPack.ComponentEntry(uid, "1"))
                                           .ToList();
        var mmcPack = new MmcPack(1, orderedComponents);
        var mmcPackStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(mmcPackStream, mmcPack, SERIALIZER_OPTIONS).ConfigureAwait(false);
        mmcPackStream.Position = 0;
        container.Attachments.Add(MultiMcHelper.PACK_INDEX_FILE_NAME, mmcPackStream);

        var instanceCfgBytes = Encoding.UTF8.GetBytes($"name={pack.Name}{Environment.NewLine}InstanceType=OneSix{Environment.NewLine}");
        container.Attachments.Add(MultiMcHelper.PACK_INSTANCE_CFG, new MemoryStream(instanceCfgBytes));

        var planner = serviceProvider.GetRequiredService<PackagePlanner>();
        var materializer = serviceProvider.GetRequiredService<PackageMaterializer>();
        var plans = await planner
                         .PlanAsync([.. setup.Packages.Where(x => x.Enabled)],
                                    new([.. setup.Rules.Where(x => x.Enabled)], Filter.FromSetup(setup)))
                         .ToListAsync()
                         .ConfigureAwait(false);
        var bag = new ConcurrentBag<(string, string)>();
        await materializer.MaterializeAsync(plans, (plan, _, path) => bag.Add((plan.RelativeTargetPath, path)))
                         .ConfigureAwait(false);
        foreach (var (rel, abs) in bag)
        {
            container.Files.Add(Path.Combine(container.OverrideDirectoryName, rel), abs);
        }

        return container;
    }

    #endregion

    private static JsonObject ConvertDocument(
        IReadOnlyList<LaunchPlanDocument.Operation> operations,
        string uid,
        string planPath,
        PackedProfileContainer container)
    {
        var patch = new JsonObject
        {
            ["formatVersion"] = 1,
            ["uid"] = uid,
            ["name"] = uid,
            ["version"] = "1"
        };
        var gameArguments = new List<string>();
        var jvmArguments = new List<string>();
        var libraries = new JsonArray();
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case LaunchPlanDocument.SetMainClassOperation set:
                    patch["mainClass"] = set.Value;
                    break;
                case LaunchPlanDocument.SetGameArgumentsOperation set:
                    gameArguments = [.. set.Values];
                    break;
                case LaunchPlanDocument.AppendGameArgumentOperation append:
                    gameArguments.Add(append.Value);
                    break;
                case LaunchPlanDocument.AppendJavaArgumentOperation append:
                    jvmArguments.Add(append.Value);
                    break;
                case LaunchPlanDocument.SetAssetIndexOperation set:
                    patch["assetIndex"] = new JsonObject
                    {
                        ["id"] = set.Value.Id,
                        ["sha1"] = set.Value.Hash?.Value,
                        ["size"] = 0,
                        ["totalSize"] = 0,
                        ["url"] = AddLocalFile(set.Value.Url, planPath, container)
                    };
                    break;
                case LaunchPlanDocument.AddLibraryOperation add:
                    libraries.Add(SerializeLibrary(add.Value, planPath, container));
                    break;
                // minecraftArguments 的 MMC 语义就是整体替换，累积后一次写出即与 clear+append 等价。
                case LaunchPlanDocument.ClearGameArgumentsOperation:
                    gameArguments.Clear();
                    break;

                // +jvmArgs 只能追加，无法表达清空；消费端会看到未被清除的上游参数。
                case LaunchPlanDocument.ClearJavaArgumentsOperation:
                    jvmArguments.Clear();
                    container.Diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Error,
                                                  "MMC export cannot express clearing JVM arguments; the exported instance keeps them.",
                                                  planPath));
                    break;
                case LaunchPlanDocument.RemoveLibrariesOperation:
                    container.Diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Error,
                                                  "MMC export cannot express removing libraries; the exported instance keeps them.",
                                                  planPath));
                    break;

                // compatibleJavaMajors 是 MMC patch 的原生字段（导入侧正是从它读的）。
                case LaunchPlanDocument.SetJavaMajorVersionOperation set:
                    patch["compatibleJavaMajors"] = new JsonArray(JsonValue.Create(set.Value));
                    break;
            }
        }

        if (gameArguments.Count > 0)
        {
            patch["minecraftArguments"] = string.Join(' ', gameArguments.Select(QuoteArgument));
        }

        if (jvmArguments.Count > 0)
        {
            patch["+jvmArgs"] = new JsonArray(jvmArguments.Select(x => JsonValue.Create(x)).ToArray());
        }

        if (libraries.Count > 0)
        {
            patch["libraries"] = libraries;
        }

        return patch;
    }

    private static JsonObject SerializeLibrary(
        LockData.Library library,
        string planPath,
        PackedProfileContainer container)
    {
        var url = AddLocalFile(library.Url, planPath, container);

        var identity = library.Id.Namespace + ":" + library.Id.Name + ":" + library.Id.Version;
        if (library.Id.Platform is not null)
        {
            identity += ":" + library.Id.Platform;
        }

        if (library.Id.Extension != "jar")
        {
            identity += "@" + library.Id.Extension;
        }

        return new JsonObject
        {
            ["name"] = identity,
            ["url"] = url,
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["sha1"] = library.Hash?.Value,
                    ["size"] = 0,
                    ["url"] = url
                }
            }
        };
    }

    private static string AddLocalFile(Uri uri, string planPath, PackedProfileContainer container)
    {
        if (uri.IsAbsoluteUri && !uri.IsFile)
        {
            return uri.ToString();
        }

        var source = uri.IsFile
                         ? uri.LocalPath
                         : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(planPath)!, uri.OriginalString));
        if (!File.Exists(source))
        {
            container.Diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                          $"MMC export could not include local launch-plan file '{uri}'.",
                                          planPath));
            return uri.ToString();
        }

        var planDirectory = Path.GetDirectoryName(planPath)!;
        var sourceDirectory = Directory.GetParent(planDirectory)?.FullName;
        var relative = sourceDirectory is not null && FileHelper.IsInDirectory(source, sourceDirectory)
                           ? Path.GetRelativePath(sourceDirectory, source)
                           : uri.IsFile ? Path.GetFileName(source) : uri.OriginalString;
        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        var attachment = Path.Combine(MultiMcHelper.PACK_MINECRAFT_DIR, relative).Replace(Path.DirectorySeparatorChar, '/');
        if (!container.Attachments.ContainsKey(attachment))
        {
            container.Attachments.Add(attachment, new MemoryStream(File.ReadAllBytes(source)));
        }

        return relative;
    }

    private static string QuoteArgument(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private static string DisambiguateUid(
        string uid,
        string source,
        IReadOnlySet<string> componentUids,
        IReadOnlySet<string> patchUids)
    {
        var used = componentUids.Concat(patchUids).ToHashSet(StringComparer.Ordinal);
        return LaunchPlanFileHelper.DisambiguateUid(uid, source, used);
    }
}
