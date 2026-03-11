using System.Collections.Frozen;
using System.Text.Json;
using Trident.Abstractions.Extensions;
using Trident.Abstractions.FileModels;
using Trident.Abstractions.Importers;
using Trident.Core.Utilities;

namespace Trident.Core.Importers;

public class TridentImporter : IProfileImporter
{
    private static string OptionsFileName => "trident.options.json";
    private static string OverridesDirectoryName => "import";

    #region IProfileImporter Members

    public string IndexFileName => "trident.index.json";

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        // https://d3ara1n.atlassian.net/jira/software/projects/POLY/boards/1?selectedIssue=POLY-39

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

        // 导入时还是要对 index 用 options 进行一遍处理
        // 对于缺失的导出 Override 这里忽略，但是对于明确不导出的 Override 项需要移除
        var overrideKeySet = options.IncludedOverrides.Where(x => !x.Enabled).Select(x => x.Key).ToFrozenSet();
        foreach (var key in index.Overrides.Keys)
        {
            if (!overrideKeySet.Contains(key))
            {
                index.RemoveOverride(key);
            }
        }

        // 如果要求移除 Source，那么就移除
        if (!options.IncludingSource)
        {
            index.Setup.Source = null;
            foreach (var entry in index.Setup.Packages)
            {
                entry.Source = null;
            }
        }

        var home = new List<string>();
        if (pack.FileNames.Contains("README.md"))
        {
            home.Add("README.md");
        }

        if (pack.FileNames.Contains("CHANGELOG.md"))
        {
            home.Add("CHANGELOG.md");
        }

        if (pack.FileNames.Contains("LICENSE.txt"))
        {
            home.Add("LICENSE.txt");
        }

        foreach (var ext in FileHelper.SupportedBitmapExtensions)
        {
            var name = $"icon.{ext}";
            if (pack.FileNames.Contains(name))
            {
                home.Add(name);
                break;
            }
        }


        var container = new ImportedProfileContainer(index,
                                                     pack
                                                        .FileNames
                                                        .Where(x => x.StartsWith(OverridesDirectoryName)
                                                                 && x != OverridesDirectoryName
                                                                 && x.Length > OverridesDirectoryName.Length + 1)
                                                        .Select(x => (x, x[(OverridesDirectoryName.Length + 1)..]))
                                                        .Where(x => !x.Item2.EndsWith('/')
                                                                 && !x.Item2.EndsWith('\\')
                                                                 && !ZipArchiveHelper.InvalidNames.Contains(x.Item2))
                                                        .ToList(),
                                                     home.Select(x => (x, x)).ToList(),
                                                     null);

        return container;
    }

    #endregion
}
