using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Utilities;

// 库文件落在哪里的唯一推导点。远程库进共享缓存（按坐标寻址、跨实例复用）；本地库原地消费。
public static class LibraryLocation
{
    // WARNING: 本地库（file:// URL，来自 launch/ 下的计划层）绝不进共享缓存。按坐标推导缓存路径
    //  会让整合包用自带的 jar 覆写 com.mojang:minecraft 这类共享条目，污染其他所有实例。
    //  原地消费使其影响面止于本实例。
    public static string Of(LockData.Library library) =>
        library.Url.IsFile
            ? library.Url.LocalPath
            : PathDef.Default.FileOfLibrary(library.Id.Namespace,
                                            library.Id.Name,
                                            library.Id.Version,
                                            library.Id.Platform,
                                            library.Id.Extension);

    // 本地库已经在磁盘上，没有可下载的来源——不进 manifest。
    public static bool NeedsDownload(LockData.Library library) => !library.Url.IsFile;
}
