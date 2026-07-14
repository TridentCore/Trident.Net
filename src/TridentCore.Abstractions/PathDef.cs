using System.Runtime.InteropServices;

namespace TridentCore.Abstractions;

public class PathDef
{
    private static readonly string USER_PROFILE =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string EFFECTIVE_HOME = LocateEffectiveHome();

    // NOTE: 启动瞬间冻结——有显式 override，或当时已存在遗留 ~/.trident，则整个进程统一用 EFFECTIVE_HOME 作单一根目录。
    // 不再用每次调用都现查的 Directory.Exists，否则运行中 ~/.trident 一旦被创建会令根目录在多根/单根之间漂移。
    private static readonly bool USE_HOME_AS_ROOT =
        EFFECTIVE_HOME != FallbackHome || Directory.Exists(FallbackHome);

    public static readonly PathDef Default = new();

    #region Platform names

    public static readonly PlatformNames TridentNames =
        new("trident", "Trident", "dev.dearain.trident");

    private static PlatformNames? _brandNames;

    public static PlatformNames BrandNames
    {
        get =>
            _brandNames
            ?? throw new InvalidOperationException(
                "PathDef.BrandNames has not been configured. Set it at application startup."
            );
        set => _brandNames = value;
    }

    private static string BrandFolder => BrandNames.Current;
    private static string HomeBrandSuffix => "." + BrandNames.Linux;

    #endregion

    #region Roots

    public string PrivateConfigDirectory() =>
        ResolveWithSuffix(HomeBrandSuffix, SystemBrandConfigPath);

    public string PrivateDataDirectory() =>
        ResolveWithSuffix(HomeBrandSuffix, SystemBrandDataPath);

    public string PrivateCacheDirectory() =>
        ResolveWithSuffix(HomeBrandSuffix, SystemBrandCachePath);

    private string ResolveDataHome() => ResolveWithSuffix("", SystemDataPath);

    private string ResolveCacheDirectory() => ResolveWithSuffix("cache", SystemCachePath);

    private string ResolveConfigDirectory() => ResolveWithSuffix("config", SystemConfigPath);

    private string ResolveWithSuffix(string suffix, Func<string> systemPath)
    {
        // NOTE: 根目录决策在进程首次访问时冻结，运行中即使 ~/.trident 被创建或删除也不再翻转
        if (USE_HOME_AS_ROOT)
            return Path.Combine(EFFECTIVE_HOME, suffix);

        return systemPath();
    }

    #endregion

    #region Instance Folder — rooted at Data

    public string InstanceDirectory => Path.Combine(ResolveDataHome(), "instances");

    public string DirectoryOfHome(string key) => Path.Combine(InstanceDirectory, key);
    public string FileOfProfile(string key) => Path.Combine(InstanceDirectory, key, "profile.json");

    public string FileOfIcon(string key, string extensionGuess) =>
        Path.Combine(InstanceDirectory, key, $"icon.{extensionGuess}");

    public string FileOfLockData(string key) => Path.Combine(InstanceDirectory, key, "data.lock.json");
    public string FileOfPackData(string key) => Path.Combine(InstanceDirectory, key, "data.pack.json");
    public string FileOfBomb(string key) => Path.Combine(InstanceDirectory, key, "_bomb_has_been_planted_");
    public string DirectoryOfBuild(string key) => Path.Combine(InstanceDirectory, key, "build");
    public string DirectoryOfNatives(string key) => Path.Combine(DirectoryOfBuild(key), "natives");
    public string DirectoryOfImport(string key) => Path.Combine(InstanceDirectory, key, "import");
    public string DirectoryOfLive(string key) => Path.Combine(InstanceDirectory, key, "live");
    public string DirectoryOfPersist(string key) => Path.Combine(InstanceDirectory, key, "persist");
    public string DirectoryOfSnapshots(string key) => Path.Combine(InstanceDirectory, key, "snapshots");

    public string DirectoryOfSnapshotObjects(string key) =>
        Path.Combine(DirectoryOfSnapshots(key), "objects");

    public string FileOfSnapshotObject(string key, string hash) =>
        Path.Combine(DirectoryOfSnapshotObjects(key), hash[..2], hash);

    #endregion

    #region Cache Folder — rooted at CacheDirectory
    public string CacheDirectory
    {
        get
        {
            field ??= ResolveCacheDirectory();
            return field;
        }
    }
    public string CacheAssetDirectory => Path.Combine(CacheDirectory, "assets");
    public string CacheIconDirectory => Path.Combine(CacheDirectory, "icons");
    public string CacheLibraryDirectory => Path.Combine(CacheDirectory, "libraries");
    public string CachePackageDirectory => Path.Combine(CacheDirectory, "packages");
    public string CacheRuntimeDirectory => Path.Combine(CacheDirectory, "runtimes");

    public string DirectoryOfRuntime(uint major) => Path.Combine(CacheRuntimeDirectory, major.ToString());

    public string FileOfRuntimeManifest(uint major) => Path.Combine(CacheRuntimeDirectory, $"{major}.json");

    public string FileOfLibrary(string ns, string name, string version, string? platform, string extension)
    {
        var nsDir = string.Join(Path.DirectorySeparatorChar, ns.Split('.'));
        return Path.Combine(CacheLibraryDirectory,
                            nsDir,
                            name,
                            version,
                            platform != null
                                ? $"{name}-{version}-{platform}.{extension}"
                                : $"{name}-{version}.{extension}");
    }

    public string FileOfPackageObject(string label, string? ns, string pid, string vid, string extension) =>
        ns != null
            ? Path.Combine(CachePackageDirectory, label, ns, pid, $"{vid}{extension}")
            : Path.Combine(CachePackageDirectory, label, pid, $"{vid}{extension}");

    public string FileOfAssetIndex(string index) => Path.Combine(CacheAssetDirectory, "indexes", $"{index}.json");

    public string FileOfAssetObject(string hash) => Path.Combine(CacheAssetDirectory, "objects", hash[..2], hash);

    public string FileOfIconObject(string hash) => Path.Combine(CacheIconDirectory, hash[..2], hash);

    #endregion

    #region System paths

    private static string SystemDataPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     TridentNames.Current);

    private static string SystemCachePath() =>
        Path.Combine(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.GetTempPath()
                    : Path.Combine(USER_PROFILE, ".cache"),
            TridentNames.Current);

    private static string SystemConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     TridentNames.Current);

    private static string SystemBrandConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BrandFolder);

    private static string SystemBrandDataPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BrandFolder);

    private static string SystemBrandCachePath()
    {
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.GetTempPath()
                : Path.Combine(USER_PROFILE, ".cache");

        return Path.Combine(basePath, BrandFolder);
    }

    #endregion

    #region Home locator

    private static string FallbackHome => Path.Combine(USER_PROFILE, ".trident");

    private static string LocateEffectiveHome()
    {
        var envHome = Environment.GetEnvironmentVariable("TRIDENT_HOME");
        if (!string.IsNullOrEmpty(envHome) && !File.Exists(envHome))
        {
            return envHome;
        }

        var dir = Directory.GetCurrentDirectory();
        string? home = null;
        while (dir is not null && Directory.Exists(dir))
        {
            var target = Path.Combine(dir, ".trident");
            if (Directory.Exists(target))
            {
                home = target;
                break;
            }

            dir = Path.GetDirectoryName(dir);
        }

        if (home != null)
            return home;

        var overrideFile = Path.Combine(USER_PROFILE, ".trident.home");
        if (File.Exists(overrideFile))
        {
            var firstLine = File.ReadLines(overrideFile).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine) && Path.IsPathRooted(firstLine) && !File.Exists(firstLine))
                return firstLine;
        }

        return FallbackHome;
    }

    #endregion
}
