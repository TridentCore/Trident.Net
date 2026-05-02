using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Engines.Deploying;

public static class LockDataBuilderExtensions
{
    public static LockDataBuilder AddParcel(
        this LockDataBuilder self,
        string label,
        string? @namespace,
        string pid,
        string vid,
        string target,
        Uri url,
        string? sha1
    ) => self.AddParcel(new(label, @namespace, pid, vid, target, url, sha1));

    // PATCH: 为了适配奇葩 PrismLauncher Meta 的多态数据
    public static LockDataBuilder AddLibraryPrismFlavor(
        this LockDataBuilder self,
        string fullname,
        Uri url
    )
    {
        var exactUrl = url.AbsoluteUri.EndsWith('/') ? url : new(url.AbsoluteUri + '/');
        // 当迁移到 TridentCore/launcher-meta 的之后移除该函数
        var id = ParseLibraryIdentity(fullname);

        var fullUrl = new Uri(
            exactUrl,
            $"{id.Namespace.Replace('.', '/')}/{id.Name}/{id.Version}/{id.Name}-{id.Version}.{id.Extension}"
        );
        return self.AddLibrary(new(id, fullUrl, null));
    }

    public static LockDataBuilder AddLibrary(
        this LockDataBuilder self,
        string fullname,
        Uri url,
        string sha1,
        bool native = false,
        bool present = true
    )
    {
        var id = ParseLibraryIdentity(fullname);

        return self.AddLibrary(new(id, url, sha1, native, present));
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
            _ => throw new NotSupportedException($"Not recognized package name format: {fullname}"),
        };
    }

    public static LockDataBuilder SetAssetIndex(
        this LockDataBuilder self,
        string id,
        Uri url,
        string sha1
    ) => self.SetAssetIndex(new(id, url, sha1));
}
