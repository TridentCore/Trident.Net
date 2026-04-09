using System.Collections.Frozen;
using System.Text.Json;
using Trident.Abstractions;
using Trident.Abstractions.Exporters;
using Trident.Abstractions.Extensions;
using Trident.Core.Utilities;

namespace Trident.Core.Exporters;

public class TridentExporter : IProfileExporter
{
    #region IProfileExporter Members

    public string Label => "trident";

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "import" };
        var overrideKeySet = pack
            .Options.IncludedOverrides.Where(x => x.Enabled)
            .Select(x => x.Key)
            .ToFrozenSet();
        var exported = pack.Profile.Clone();

        // 根据 Options 对 Profile 进行过滤
        foreach (var key in exported.Overrides.Keys)
        {
            if (!overrideKeySet.Contains(key))
            {
                exported.RemoveOverride(key);
            }
        }

        if (!pack.Options.IncludingSource)
        {
            exported.Setup.Source = null;
            foreach (var entry in exported.Setup.Packages)
            {
                entry.Source = null;
            }
        }

        if (!pack.Options.IncludingTags)
        {
            foreach (var entry in exported.Setup.Packages)
            {
                entry.Tags.Clear();
            }
        }

        var homeDir = PathDef.Default.DirectoryOfHome(pack.Key);
        var licenseFile = Path.Combine(homeDir, "LICENSE.txt");
        if (File.Exists(licenseFile))
        {
            var license = new MemoryStream(
                await File.ReadAllBytesAsync(licenseFile).ConfigureAwait(false)
            );
            container.Attachments.Add("LICENSE.txt", license);
        }

        var readmeFile = Path.Combine(homeDir, "README.md");
        if (File.Exists(readmeFile))
        {
            var readme = new MemoryStream(
                await File.ReadAllBytesAsync(readmeFile).ConfigureAwait(false)
            );
            container.Attachments.Add("README.md", readme);
        }

        var changelogFile = Path.Combine(homeDir, "CHANGELOG.md");
        if (File.Exists(changelogFile))
        {
            var changelog = new MemoryStream(
                await File.ReadAllBytesAsync(changelogFile).ConfigureAwait(false)
            );
            container.Attachments.Add("CHANGELOG.md", changelog);
        }

        var iconFile = InstanceHelper.PickIcon(pack.Key);
        if (iconFile != null && File.Exists(iconFile))
        {
            var icon = new MemoryStream(
                await File.ReadAllBytesAsync(iconFile).ConfigureAwait(false)
            );
            container.Attachments.Add(Path.GetFileName(iconFile), icon);
        }

        var index = new MemoryStream();
        await JsonSerializer
            .SerializeAsync(index, exported, FileHelper.SerializerOptions)
            .ConfigureAwait(false);
        index.Position = 0;
        var options = new MemoryStream();
        await JsonSerializer
            .SerializeAsync(options, pack.Options, FileHelper.SerializerOptions)
            .ConfigureAwait(false);
        options.Position = 0;
        container.Attachments.Add("trident.index.json", index);
        container.Attachments.Add("trident.options.json", options);

        return container;
    }

    #endregion
}
