using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;
using TridentCore.Core.Services.Instances;
using TridentCore.Core.Utilities;
using TridentCore.Pref;

namespace TridentCore.Cli.Operations;

internal static class InstanceOperation
{
    public static IReadOnlyList<InstanceSummary> List(ProfileManager profileManager) =>
        profileManager
           .Profiles.Select(x => new InstanceSummary(x.Item1,
                                                     x.Item2.Name,
                                                     x.Item2.Setup.Version,
                                                     x.Item2.Setup.Loader,
                                                     x.Item2.Setup.Source,
                                                     x.Item2.Setup.Packages.Count,
                                                     PathDef.Default.DirectoryOfHome(x.Item1)))
           .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
           .ToArray();

    public static async Task<InstanceDetail> Inspect(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string instance,
        string? profile)
    {
        const int previewLimit = 5;
        var ctx = resolver.Resolve(instance, profile);
        var entries = ctx.Profile.Setup.Packages;
        var preview = entries.Take(previewLimit).ToList();

        var resolved = preview.Count > 0
                           ? await PackageDtos.ResolveEntriesAsync(preview, repositories, ctx).ConfigureAwait(false)
                           : [];

        return new(ctx.Key,
                   ctx.Profile.Name,
                   ctx.Profile.Setup.Version,
                   ctx.Profile.Setup.Loader,
                   ctx.Profile.Setup.Source,
                   ctx.InstancePath,
                   ctx.ProfilePath,
                   entries.Count,
                   [
                       .. resolved.Select(p => new PackagePreview(p.Pref,
                                                                  p.Enabled,
                                                                  p.Source,
                                                                  p.Tags,
                                                                  p.ProjectName,
                                                                  p.Author,
                                                                  p.Kind))
                   ],
                   Math.Max(0, entries.Count - previewLimit));
    }

    public static InstanceCreateResult Create(
        ProfileManager profileManager,
        string name,
        string version,
        string? loader,
        string? identity)
    {
        var key = profileManager.RequestKey(InstanceIdentityValidator.EnsureValid(identity ?? name));
        var profile = new Profile { Name = name, Setup = new() { Version = version, Source = null, Loader = loader } };
        profileManager.Add(key, profile);
        return new(key.Key, profile.Name, profile.Setup.Version, profile.Setup.Loader);
    }

    public static InstanceUnlockResult Unlock(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        var oldSource = guard.Value.Setup.Source;
        guard.Value.Setup.Source = null;
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new(ctx.Key, oldSource);
    }

    public static InstanceDeleteResult Delete(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var bomb = PathDef.Default.FileOfBomb(ctx.Key);
        var dir = Path.GetDirectoryName(bomb);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(bomb, "delete requested by trident cli");
        profileManager.Remove(ctx.Key);
        return new(ctx.Key, bomb);
    }

    public static InstanceResetResult Reset(
        InstanceContextResolver resolver,
        InstanceManager instanceManager,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        if (instanceManager.IsInUse(ctx.Key))
        {
            throw new CliException($"Instance '{ctx.Key}' is currently in use.", ExitCodes.USAGE);
        }

        var deleted = new List<string>();
        DeleteDirectory(PathDef.Default.DirectoryOfBuild(ctx.Key), deleted);
        DeleteFile(PathDef.Default.FileOfLockData(ctx.Key), deleted);
        return new(ctx.Key, deleted);
    }

    public static async Task<InstanceBuildResult> BuildAsync(
        InstanceContextResolver resolver,
        InstanceManager instanceManager,
        string instance,
        string? profile,
        bool fastMode,
        bool resolveDependency,
        bool fullCheck,
        string? javaHome)
    {
        var ctx = resolver.Resolve(instance, profile);
        var options = new DeployOptions(fastMode, resolveDependency, fullCheck);
        var locator = JavaHelper.MakeLocator(_ => javaHome);
        var tracker = instanceManager.Deploy(ctx.Key, options, locator);
        await TrackerAwaiter.AwaitCompletionAsync(tracker, CancellationToken.None).ConfigureAwait(false);
        return new(ctx.Key, "finished");
    }

    public static async Task<InstanceExportResult> ExportAsync(
        InstanceContextResolver resolver,
        ExporterAgent exporterAgent,
        string instance,
        string? profile,
        string format,
        string type,
        string? name,
        string author,
        string version,
        string output,
        bool noTags)
    {
        var ctx = resolver.Resolve(instance, profile);
        var options = new PackData
        {
            IncludingSource = string.Equals(type, "offline", StringComparison.OrdinalIgnoreCase),
            IncludingTags = !noTags,
            OfflineMode = string.Equals(type, "offline", StringComparison.OrdinalIgnoreCase)
        };

        using var container = await exporterAgent
                                   .ExportAsync(options, format, ctx.Key, name ?? ctx.Profile.Name, author, version)
                                   .ConfigureAwait(false);

        var outputPath = Path.GetFullPath(output);
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await using var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await exporterAgent.PackCompressedAsync(file, container).ConfigureAwait(false);
        await file.FlushAsync().ConfigureAwait(false);

        return new(ctx.Key, format, type, outputPath);
    }

    public static async Task<InstanceImportResult> ImportAsync(
        ProfileManager profileManager,
        ImporterAgent importerAgent,
        string path,
        string? name,
        string? identity)
    {
        var sourcePath = Path.GetFullPath(path);
        if (!File.Exists(sourcePath))
        {
            throw new CliException($"Pack file '{sourcePath}' was not found.", ExitCodes.NOT_FOUND);
        }

        await using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory).ConfigureAwait(false);
        memory.Position = 0;

        using var pack = new CompressedProfilePack(memory);
        var container = await importerAgent.ImportAsync(pack).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(name))
        {
            container.Profile.Name = name;
        }

        var id = InstanceIdentityValidator.EnsureValid(identity
                                                    ?? container.Profile.Name
                                                    ?? Path.GetFileNameWithoutExtension(sourcePath));
        var key = profileManager.RequestKey(id);
        await importerAgent.ExtractFilesAsync(key.Key, container, pack).ConfigureAwait(false);
        profileManager.Add(key, container.Profile);

        return new(key.Key,
                   container.Profile.Name ?? id,
                   container.Profile.Setup.Version,
                   container.Profile.Setup.Loader,
                   sourcePath);
    }

    public static async Task<InstallTracker> StartInstallAsync(
        InstanceManager instanceManager,
        RepositoryAgent repositories,
        string pref,
        string? identity)
    {
        var parsed = PackageCliHelper.ParsePref(pref);

        var project = await repositories.QueryAsync(parsed.ToProjectIdentifier()).ConfigureAwait(false);
        if (project.Kind != ResourceKind.Modpack)
        {
            throw new
                CliException($"'{pref}' is a {project.Kind.ToString().ToLowerInvariant()}, not a modpack. Use `package add` to add non-modpack packages.",
                             ExitCodes.USAGE);
        }

        return instanceManager.Install(identity ?? project.ProjectName,
                                       parsed.Repository,
                                       parsed.Namespace,
                                       parsed.Identity,
                                       parsed.Version);
    }

    private static void DeleteDirectory(string path, IList<string> deleted)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, true);
        deleted.Add(path);
    }

    private static void DeleteFile(string path, IList<string> deleted)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        deleted.Add(path);
    }
}

public sealed record InstanceSummary(
    string Key,
    string Name,
    string Version,
    string? Loader,
    string? Source,
    int PackageCount,
    string Path);

public sealed record InstanceDetail(
    string Key,
    string Name,
    string Version,
    string? Loader,
    string? Source,
    string InstancePath,
    string ProfilePath,
    int PackageCount,
    IReadOnlyList<PackagePreview> Packages,
    int HiddenPackageCount);

public sealed record PackagePreview(
    string Pref,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags,
    string? ProjectName,
    string? Author,
    ResourceKind? Kind) : IPackageTableRow;

internal sealed record InstanceCreateResult(string Key, string Name, string Version, string? Loader);

internal sealed record InstanceUnlockResult(string Key, string? OldSource);

internal sealed record InstanceDeleteResult(string Key, string Bomb);

internal sealed record InstanceResetResult(string Key, IReadOnlyList<string> Deleted);

internal sealed record InstanceBuildResult(string Key, string State);

internal sealed record InstanceExportResult(string Key, string Format, string Type, string Output);

internal sealed record InstanceImportResult(string Key, string Name, string Version, string? Loader, string Path);
