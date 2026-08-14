using System.Collections.Frozen;
using System.Text.Json;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class TridentImporter : IProfileImporter
{
    private static string IndexFileName => "trident.index.json";
    private static string OptionsFileName => "trident.options.json";
    private static string OverridesDirectoryName => "import";
    // NOTE: 整合包注入这两个 key 即可执行任意程序或劫持 Java 运行时，导入时无条件剔除。
    private static readonly FrozenSet<string> UNSAFE_OVERRIDE_KEYS =
        new[] { Profile.OVERRIDE_BEHAVIOR_COMMAND_WRAPPER, Profile.OVERRIDE_JAVA_HOME }.ToFrozenSet();

    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.RootPrefix is null && pack.FileNames.Contains(IndexFileName);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        // NOTE: 相关修复见 POLY-39（https://d3ara1n.atlassian.net/browse/POLY-39）。

        await using var indexStream = pack.Open(IndexFileName);
        await using var optionsStream = pack.Open(OptionsFileName);
        var index = await JsonSerializer
                         .DeserializeAsync<Profile>(indexStream, FileHelper.SerializerOptions)
                         .ConfigureAwait(false);
        if (index is null)
        {
            throw new FormatException($"{IndexFileName} is not a valid manifest");
        }

        var options = await JsonSerializer
                           .DeserializeAsync<PackData>(optionsStream, FileHelper.SerializerOptions)
                           .ConfigureAwait(false);
        if (options is null)
        {
            throw new FormatException($"{OptionsFileName} is not a valid manifest");
        }

        // 导入是不可信输入，只接受导出端明确启用的 Override。
        var included = options.IncludedOverrides.Where(x => x.Enabled).Select(x => x.Key).ToFrozenSet();
        foreach (var key in index.Overrides.Keys)
        {
            if (!included.Contains(key) || UNSAFE_OVERRIDE_KEYS.Contains(key))
            {
                index.RemoveOverride(key);
            }
        }

        if (!options.IncludingSource)
        {
            index.Setup.Source = null;
            foreach (var entry in index.Setup.Packages)
            {
                entry.Source = null;
            }
        }

        if (!options.IncludingTags)
        {
            foreach (var entry in index.Setup.Packages)
            {
                entry.Tags.Clear();
            }
        }

        var container = new ImportedProfileContainer(index,
                                                     [
                                                         .. pack
                                                           .FileNames
                                                           .Where(x => x.StartsWith(OverridesDirectoryName)
                                                                    && x != OverridesDirectoryName
                                                                    && x.Length > OverridesDirectoryName.Length + 1)
                                                           .Select(x => (x, x[(OverridesDirectoryName.Length + 1)..]))
                                                           .Where(x => ZipArchiveHelper.IsExtractableEntry(x.Item2))
                                                     ],
                                                     [
                                                         ("README.md", "README.md"),
                                                         ("CHANGELOG.md", "CHANGELOG.md"),
                                                         ("LICENSE.txt", "LICENSE.txt"),
                                                         .. FileHelper.SupportedBitmapExtensions.Select(ext => ($"icon.{ext}", $"icon.{ext}"))
                                                     ],
                                                     null);

        return container;
    }

    #endregion
}
