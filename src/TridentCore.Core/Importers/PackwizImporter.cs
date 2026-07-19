using Tomlyn.Model;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class PackwizImporter : IProfileImporter
{
    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.FileNames.Contains((pack.RootPrefix ?? string.Empty) + PackwizHelper.INDEX_FILE_NAME);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        var prefix = pack.RootPrefix ?? string.Empty;
        await using var manifestStream = pack.Open(prefix + PackwizHelper.INDEX_FILE_NAME);
        using var manifestReader = new StreamReader(manifestStream);
        var manifest = PackwizHelper.ParsePackManifest(
            await manifestReader.ReadToEndAsync().ConfigureAwait(false)
        );

        if (string.IsNullOrEmpty(manifest.Minecraft))
            throw new FormatException($"{PackwizHelper.INDEX_FILE_NAME} declares no minecraft version");

        var source = pack.Reference is not null ? PackageHelper.ToPref(pack.Reference) : null;
        var loader = manifest.Loader is { } l ? LoaderHelper.ToLurl(l.Identity, l.Version) : null;

        var packages = new List<Profile.Rice.Entry>();
        foreach (var name in pack.FileNames)
        {
            if (!name.EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase))
                continue;

            await using var s = pack.Open(name);
            using var r = new StreamReader(s);
            var mod = PackwizHelper.Parse(await r.ReadToEndAsync().ConfigureAwait(false));

            if (PackwizHelper.IsServerOnly(mod))
                continue;

            var pref = PackwizHelper.TryExtractPref(mod);
            if (pref is null)
                continue;

            packages.Add(new() { Pref = pref, Enabled = true, Source = source });
        }

        var indexFullName = prefix + PackwizHelper.INDEX_FILE_NAME;
        var indexTomlName = prefix + "index.toml";
        var importFiles = pack.FileNames
            .Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
            .Select(x => (Source: x, Target: x[prefix.Length..]))
            .Where(p => !string.IsNullOrEmpty(p.Target))
            .Where(p => !p.Target.EndsWith('/') && !p.Target.EndsWith('\\'))
            .Where(p => p.Source != indexFullName && p.Source != indexTomlName)
            .Where(p => !p.Target.EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !ZipArchiveHelper.InvalidNames.Contains(p.Target))
            .Select(p => (p.Source, p.Target))
            .ToList();

        return new(
            new()
            {
                Name = string.IsNullOrEmpty(manifest.Name) ? "Imported packwiz modpack" : manifest.Name,
                Setup = new()
                {
                    Source = source,
                    Version = manifest.Minecraft,
                    Loader = loader,
                    Packages = packages,
                },
            },
            importFiles,
            [],
            pack.Reference?.Thumbnail
        );
    }

    #endregion
}
