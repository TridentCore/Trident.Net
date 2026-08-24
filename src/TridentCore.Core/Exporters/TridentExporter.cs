using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Pref.Building;

namespace TridentCore.Core.Exporters;

public class TridentExporter(IServiceProvider serviceProvider) : IProfileExporter
{
    #region IProfileExporter Members

    public string Label => "trident";

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "import" };
        var overrideKeySet = pack.Options.IncludedOverrides.Where(x => x.Enabled).Select(x => x.Key).ToFrozenSet();
        var exported = pack.Profile;

        foreach (var key in exported.Overrides.Keys)
        {
            if (!overrideKeySet.Contains(key))
            {
                exported.RemoveOverride(key);
            }
        }

        if (!pack.Options.IncludingSource)
        {
            // NOTE: IncludingSource 语义是「不关联原始整合包」——只针对整合包型（pref://）来源：
            //  剔除 setup 级引用；包级 pref 来源连同 SourceOrders 确定性映射为 collection，
            //  使覆盖层叠（(Pref, Source) 身份 + 叠加优先级）在消费者侧原样存活。
            exported.Setup.Source = null;
            var profileManager = serviceProvider.GetRequiredService<ProfileManager>();
            var namesBySource = profileManager
                               .Profiles.Select(x => (x.Item2.Setup.Source, x.Item2.Name))
                               .Where(x => x.Source is not null)
                               .DistinctBy(x => x.Source)
                               .ToDictionary(x => x.Source!, x => x.Name);
            string Sever(string? source)
            {
                // NOTE: 非 pref 戳（collection 等）不是整合包来源，原样保留。
                if (source is null || !InternalUriHelper.IsKind(source, Builder.Scheme))
                {
                    return source!;
                }

                var fallback = PackageHelper.TryParse(source, out var id) ? id.Identity : source;
                return CollectionHelper.ToUri(namesBySource.TryGetValue(source, out var name) ? name : fallback);
            }

            foreach (var entry in exported.Setup.Packages)
            {
                entry.Source = Sever(entry.Source);
            }

            var orders = exported.Setup.SourceOrders;
            for (var i = 0; i < orders.Count; i++)
            {
                orders[i] = Sever(orders[i]);
            }
        }

        if (!pack.Options.IncludingTags)
        {
            foreach (var entry in exported.Setup.Packages)
            {
                entry.Tags.Clear();
            }
        }

        if (pack.Options.OfflineMode)
        {
            var setup = exported.Setup;
            var planner = serviceProvider.GetRequiredService<PackagePlanner>();
            var materializer = serviceProvider.GetRequiredService<PackageMaterializer>();
            var plans = await planner
                             .PlanAsync([.. setup.Packages.Where(x => x.Enabled)],
                                        new([.. setup.Rules.Where(x => x.Enabled)], Filter.FromSetup(setup)))
                             .ToListAsync()
                             .ConfigureAwait(false);
            var bag = new ConcurrentBag<(string, string)>();
            await materializer
                 .MaterializeAsync(plans,
                                   (plan, _, path) =>
                                   {
                                       bag.Add((plan.RelativeTargetPath, path));
                                   })
                 .ConfigureAwait(false);
            foreach (var (rel, abs) in bag)
            {
                var relative = Path.Combine(container.OverrideDirectoryName, rel);
                container.Files.Add(relative, abs);
            }

            exported.Setup.Packages.Clear();
        }

        var homeDir = PathDef.Default.DirectoryOfHome(pack.Key);
        var licenseFile = Path.Combine(homeDir, "LICENSE.txt");
        if (File.Exists(licenseFile))
        {
            var license = new MemoryStream(await File.ReadAllBytesAsync(licenseFile).ConfigureAwait(false));
            container.Attachments.Add("LICENSE", license);
        }

        var readmeFile = Path.Combine(homeDir, "README.md");
        if (File.Exists(readmeFile))
        {
            var readme = new MemoryStream(await File.ReadAllBytesAsync(readmeFile).ConfigureAwait(false));
            container.Attachments.Add("README.md", readme);
        }

        var changelogFile = Path.Combine(homeDir, "CHANGELOG.md");
        if (File.Exists(changelogFile))
        {
            var changelog = new MemoryStream(await File.ReadAllBytesAsync(changelogFile).ConfigureAwait(false));
            container.Attachments.Add("CHANGELOG.md", changelog);
        }

        var iconFile = InstanceHelper.PickIcon(pack.Key);
        if (iconFile != null && File.Exists(iconFile))
        {
            var icon = new MemoryStream(await File.ReadAllBytesAsync(iconFile).ConfigureAwait(false));
            container.Attachments.Add(Path.GetFileName(iconFile), icon);
        }

        var index = new MemoryStream();
        await JsonSerializer.SerializeAsync(index, exported, FileHelper.SerializerOptions).ConfigureAwait(false);
        index.Position = 0;
        var options = new MemoryStream();
        await JsonSerializer.SerializeAsync(options, pack.Options, FileHelper.SerializerOptions).ConfigureAwait(false);
        options.Position = 0;
        container.Attachments.Add("trident.index.json", index);
        container.Attachments.Add("trident.options.json", options);

        return container;
    }

    #endregion
}
