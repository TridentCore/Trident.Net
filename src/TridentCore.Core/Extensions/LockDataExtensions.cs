using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Igniters;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Extensions;

public static class LockDataExtensions
{
    public static Igniter MakeIgniter(this LockData.ArtifactData self, string? key = null)
    {
        var igniter = new Igniter { ArgumentResolver = argument => LaunchArgumentHelper.Expand(argument, self, key) };

        foreach (var argument in self.GameArguments.SelectMany(x => x))
        {
            igniter.AddGameArgument(argument);
        }

        foreach (var argument in self.JavaArguments.SelectMany(x => x))
        {
            igniter.AddJvmArgument(argument);
        }

        foreach (var library in self.AllLibraries().Where(x => x.IsPresent))
        {
            igniter.AddLibrary(library.FilePath(key));
        }

        foreach (var agent in self.Agents)
        {
            igniter.AddJavaAgent(new(agent.Library.Id, agent.Library.FilePath(key), agent.Arguments));
        }

        igniter.SetMainClass(self.MainClass).SetAssetIndex(self.AssetIndex.Id);

        return igniter;
    }

    public static IEnumerable<LockData.Library> AllLibraries(this LockData.ArtifactData artifact) =>
        artifact.MainJar is { } main ? artifact.Libraries.Append(main) : artifact.Libraries;

    public static string FilePath(this LockData.Library library, string? key)
    {
        if (library.LocalPath is { } local)
        {
            if (key is null || !local.StartsWith("patches/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("A local patch library requires an instance and a patches-relative path.");
            }
            return PatchHelper.ResolvePath(PathDef.Default.DirectoryOfHome(key), local);
        }
        var path = PathDef.Default.FileOfLibrary(library.Id.Namespace, library.Id.Name, library.Id.Version,
                                                  library.Id.Platform, library.Id.Extension);
        if (library.CacheKey is { } cacheKey)
        {
            if (cacheKey.Length != 64 || !cacheKey.All(char.IsAsciiHexDigit))
            {
                throw new InvalidDataException("Invalid patch library cache key.");
            }
            path = Path.Combine(PathDef.Default.CacheLibraryDirectory, ".patches", cacheKey,
                                Path.GetRelativePath(PathDef.Default.CacheLibraryDirectory, path));
        }
        return path;
    }

    // NOTE: 由锁定包的冻结规则 + 解析结果推导的 build 内相对目标。集中于此，
    //  FlattenPackages（冲突分组）与 GenerateManifest（物化）不会算出不同值。
    public static string RelativeTarget(this LockData.LockedPackage self) =>
        PackagePathHelper.RelativeTarget(self.Rule.Normalizing,
                                         self.Rule.Destination,
                                         self.Resolved.ProjectName,
                                         self.Resolved.FileName,
                                         self.Resolved.Kind);

    public static void AddLibrary(
        this IList<LockData.Library> libs,
        string fullname,
        Uri url,
        FileHash? hash,
        bool native = false,
        bool present = true) =>
        libs.Add(new(ParseLibraryIdentity(fullname), url, hash, native, present));

    // HACK: 适配 PrismLauncher Meta 的奇葩多态数据。
    public static void AddLibraryPrismFlavor(this IList<LockData.Library> libs, string fullname, Uri url)
    {
        var exactUrl = url.AbsoluteUri.EndsWith('/') ? url : new(url.AbsoluteUri + '/');
        // TODO: 迁移到 TridentCore/launcher-meta 后移除该函数。
        var id = ParseLibraryIdentity(fullname);

        var fullUrl = new Uri(exactUrl,
                              $"{id.Namespace.Replace('.', '/')}/{id.Name}/{id.Version}/{id.Name}-{id.Version}.{id.Extension}");
        libs.Add(new(id, fullUrl, null));
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
