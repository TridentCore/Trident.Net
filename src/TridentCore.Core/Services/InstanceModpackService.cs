using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Importers;
using TridentCore.Core.Facilities;
using TridentCore.Core.Services.Profiles;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public sealed class InstanceModpackService(
    ProfileManager profiles,
    ImporterAgent importers,
    IHttpClientFactory clients,
    ILogger<InstanceModpackService> logger)
{
    private static readonly HashSet<string> RESERVED_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        "profile.json", "data.lock.json", "data.pack.json", "build", "import", "persist", "launch", "snapshots",
        "_bomb_has_been_planted_"
    };

    public Task InstallAsync(ReservedKey key, CompressedProfilePack pack, ImportedProfileContainer container, CancellationToken token,
                             IReadOnlyDictionary<string, byte[]>? attachments = null) =>
        InstallAsync(key, pack, container, token, files: null, attachments: attachments);

    internal async Task InstallAsync(ReservedKey key, CompressedProfilePack pack, ImportedProfileContainer container,
                                     CancellationToken token, FileTransaction.FileOperations? files,
                                     IReadOnlyDictionary<string, byte[]>? attachments = null)
    {
        using (key)
        {
            var home = PathDef.Default.DirectoryOfHome(key.Key);
            FileTransaction.EnsureInstanceReady(home);
            if (Path.Exists(home)) throw new IOException($"Installation target '{home}' already exists; existing files will not be reused");
            Directory.CreateDirectory(PathDef.Default.InstanceDirectory);
            var prepared = profiles.PrepareAdd(key, container.Profile.Clone());
            using (var transaction = new FileTransaction(PathDef.Default.InstanceDirectory, logger, files,
                       FileTransaction.InstallWorkspaceName(key.Key)))
            {
                var staged = transaction.StagePath(key.Key);
                await StageAsync(staged, null, pack, container, token, attachments).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(staged, "profile.json"),
                    JsonSerializer.Serialize(prepared.Handle.Value, FileHelper.SerializerOptions), token).ConfigureAwait(false);
                transaction.CreateDirectory(key.Key);
                transaction.Commit(prepared.Publish, token);
            }
            prepared.Notify();
            logger.LogInformation("Instance installation committed for {key}", key.Key);
        }
    }

    internal async Task ApplyAsync(string key, CompressedProfilePack pack, ImportedProfileContainer container,
                                  CancellationToken token, FileTransaction.FileOperations? files = null)
    {
        var home = PathDef.Default.DirectoryOfHome(key);
        FileTransaction.EnsureInstanceReady(home);
        var prepared = profiles.PrepareUpdate(key, container.Profile);
        using (var transaction = new FileTransaction(home, logger, files))
        {
            var attachments = await StageAsync(transaction.StagingDirectory, home, pack, container, token).ConfigureAwait(false);
            var import = PathDef.Default.DirectoryOfImport(key);
            if (Directory.Exists(import))
            {
                foreach (var source in Directory.EnumerateFiles(import, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false
                }))
                {
                    token.ThrowIfCancellationRequested();
                    var relative = Path.Combine("build", Path.GetRelativePath(import, source));
                    var live = Path.Combine(home, relative);
                    if (File.Exists(live) && !FileTransaction.HasLinkBelow(live, home)) transaction.RemoveFile(relative);
                }
            }
            transaction.ReplaceDirectory("import");
            if (container.ReplacesLaunchImport) transaction.ReplaceDirectory("launch/import");
            foreach (var target in attachments) transaction.ReplaceFile(target);
            transaction.RemoveFile("data.lock.json");
            await File.WriteAllTextAsync(transaction.StagePath("profile.json"),
                JsonSerializer.Serialize(prepared.Value, FileHelper.SerializerOptions), token).ConfigureAwait(false);
            transaction.ReplaceFile("profile.json");
            transaction.Commit(prepared.Publish, token);
        }
        prepared.Notify();
        logger.LogInformation("Instance update committed for {key}", key);
    }

    private async Task<IReadOnlyList<string>> StageAsync(string staged, string? home, CompressedProfilePack pack,
                                                        ImportedProfileContainer container, CancellationToken token,
                                                        IReadOnlyDictionary<string, byte[]>? additionalAttachments = null)
    {
        var stagedImport = Path.Combine(staged, "import");
        Directory.CreateDirectory(stagedImport);
        await importers.ExtractToAsync(stagedImport, container.ImportFileNames, pack, token).ConfigureAwait(false);
        ValidateStaged(stagedImport, container.ImportFileNames, pack);

        var launchFiles = container.LaunchFileNames;
        var generated = container.GeneratedLaunchFiles ?? [];
        if (!container.ReplacesLaunchImport && (launchFiles.Count > 0 || generated.Count > 0))
            throw new InvalidDataException("Launch files require ownership of the imported launch directory");
        if (container.ReplacesLaunchImport)
        {
            var stagedLaunch = Path.Combine(staged, "launch");
            var stagedLaunchImport = Path.Combine(stagedLaunch, "import");
            Directory.CreateDirectory(stagedLaunchImport);
            foreach (var target in launchFiles.Select(x => x.Target).Concat(generated.Select(x => x.Target)))
            {
                if (!target.StartsWith(LaunchDefinitionHelper.IMPORT_PREFIX, StringComparison.Ordinal)
                 || !FileHelper.IsInDirectory(Path.Combine(stagedLaunch, target), stagedLaunchImport))
                    throw new InvalidDataException($"Launch file '{target}' is outside the imported launch directory");
            }
            await importers.ExtractToAsync(stagedLaunch, launchFiles, pack, token).ConfigureAwait(false);
            await importers.WriteGeneratedToAsync(stagedLaunch, generated, token).ConfigureAwait(false);
            ValidateStaged(stagedLaunch, launchFiles, pack);
            foreach (var (target, content) in generated)
            {
                if (new FileInfo(Path.Combine(stagedLaunch, target)).Length != content.LongLength)
                    throw new InvalidDataException($"Generated launch file '{target}' is truncated");
            }
            _ = LaunchDefinitionSnapshot.LoadDirectory(stagedLaunch, includeUser: false);
        }

        var attachments = container.HomeFileNames.Where(x => pack.LengthOf(x.Source) is not null).ToList();
        foreach (var (_, target) in attachments) ValidateAttachment(target);
        await importers.ExtractToAsync(staged, attachments, pack, token).ConfigureAwait(false);
        ValidateStaged(staged, attachments, pack);
        var targets = attachments.Select(x => x.Target).ToList();
        foreach (var (target, content) in additionalAttachments ?? new Dictionary<string, byte[]>())
        {
            ValidateAttachment(target);
            await File.WriteAllBytesAsync(Path.Combine(staged, target), content, token).ConfigureAwait(false);
            if (!targets.Contains(target)) targets.Add(target);
        }
        if (container.IconUrl is { } icon && (home is null || !Directory.EnumerateFiles(home, "icon.*").Any())
            && !Directory.EnumerateFiles(staged, "icon.*").Any())
        {
            using var client = clients.CreateClient();
            targets.Add(await importers.ExtractIconAsync(staged, icon, client, token).ConfigureAwait(false));
        }
        return targets;
    }

    private static void ValidateAttachment(string target)
    {
        if (!FileHelper.IsFileName(target) || target.StartsWith('.') || RESERVED_NAMES.Contains(target))
            throw new InvalidDataException($"Invalid instance attachment '{target}'");
    }

    private static void ValidateStaged(string directory, IReadOnlyList<(string Source, string Target)> files, CompressedProfilePack pack)
    {
        foreach (var (source, target) in files)
        {
            if (pack.LengthOf(source) is not { } expected) continue;
            var file = Path.Combine(directory, target);
            if (!File.Exists(file) || new FileInfo(file).Length != expected)
                throw new InvalidDataException($"Staged file '{target}' is missing or truncated");
        }
    }
}
