using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Igniters;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Extensions;

public static class LockDataExtensions
{
    public static Igniter MakeIgniter(this LockData.ArtifactData self)
    {
        var igniter = new Igniter();

        foreach (var argument in self.GameArguments)
        {
            igniter.AddGameArgument(argument);
        }

        foreach (var argument in self.JavaArguments)
        {
            igniter.AddJvmArgument(argument);
        }

        foreach (var library in self.Libraries.Where(x => x.IsPresent))
        {
            igniter.AddLibrary(PathDef.Default.FileOfLibrary(library.Id.Namespace,
                                                             library.Id.Name,
                                                             library.Id.Version,
                                                             library.Id.Platform,
                                                             library.Id.Extension));
        }

        igniter.SetMainClass(self.MainClass).SetAssetIndex(self.AssetIndex.Id);

        return igniter;
    }

    // NOTE: 由锁定包的冻结规则 + 解析结果推导的 build 内相对目标。集中于此，
    //  FlattenPackages（冲突分组）与 GenerateManifest（物化）不会算出不同值。
    public static string RelativeTarget(this LockData.LockedPackage self) =>
        PackagePathHelper.RelativeTarget(self.Rule.Normalizing,
                                         self.Rule.Destination,
                                         self.Resolved.ProjectName,
                                         self.Resolved.FileName,
                                         self.Resolved.Kind);

    // NOTE: 可变列表式库累加，与平台计算 artifact 重建时的去重规则一致
    //  （vanilla 与 loader 都会增量加库）。
    public static void AddLibrary(this IList<LockData.Library> libs, LockData.Library library)
    {
        // NOTE: 允许仅 IsNative 不同的并存，不允许仅 IsPresent 不同的并存；IsPresent==true 优先。
        var found = libs.FirstOrDefault(x => x.Id.Namespace == library.Id.Namespace
                                          && x.Id.Name == library.Id.Name
                                          && x.Id.Platform == library.Id.Platform
                                          && x.Id.Extension == library.Id.Extension
                                          && x.IsNative == library.IsNative);
        if (found != null)
        {
            if (found.Id.Version == library.Id.Version)
            {
                if (library.IsPresent)
                {
                    libs.Remove(found);
                }
                else
                {
                    return;
                }
            }
            else if (found.IsPresent && library.IsPresent)
            {
                libs.Remove(found);
            }
        }

        libs.Add(library);
    }

    public static void AddLibrary(
        this IList<LockData.Library> libs,
        string fullname,
        Uri url,
        FileHash? hash,
        bool native = false,
        bool present = true) =>
        libs.AddLibrary(new(ParseLibraryIdentity(fullname), url, hash, native, present));

    // HACK: 适配 PrismLauncher Meta 的奇葩多态数据。
    public static void AddLibraryPrismFlavor(this IList<LockData.Library> libs, string fullname, Uri url)
    {
        var exactUrl = url.AbsoluteUri.EndsWith('/') ? url : new(url.AbsoluteUri + '/');
        // TODO: 迁移到 TridentCore/launcher-meta 后移除该函数。
        var id = ParseLibraryIdentity(fullname);

        var fullUrl = new Uri(exactUrl,
                              $"{id.Namespace.Replace('.', '/')}/{id.Name}/{id.Version}/{id.Name}-{id.Version}.{id.Extension}");
        libs.AddLibrary(new(id, fullUrl, null));
    }

    private static LockData.Library.Identity ParseLibraryIdentity(string fullname)
    {
        var extension = "jar";
        var index = fullname.IndexOf('@');
        if (index > 0)
        {
            extension = fullname[(index + 1)..];
            fullname = fullname[..index];
        }

        var split = fullname.Split(':');
        return split.Length switch
        {
            4 => new(split[0], split[1], split[2], split[3], extension),
            3 => new(split[0], split[1], split[2], null, extension),
            _ => throw new NotSupportedException($"Not recognized package name format: {fullname}")
        };
    }
}
