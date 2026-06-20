using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TridentCore.Abstractions;

public class PathDef
{
    private static readonly string USER_PROFILE =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string EFFECTIVE_HOME = LocateEffectiveHome();

    public static readonly PathDef Default = new();

    #region Roots

    public string PrivateConfigDirectory(string brand) =>
        ResolveWithSuffix($".{brand.ToLowerInvariant()}", () => SystemBrandConfigPath(brand));

    public string PrivateDataDirectory(string brand) =>
        ResolveWithSuffix($".{brand.ToLowerInvariant()}", () => SystemBrandDataPath(brand));

    public string PrivateCacheDirectory(string brand) =>
        ResolveWithSuffix($".{brand.ToLowerInvariant()}", () => SystemBrandCachePath(brand));

    private string ResolveDataHome() => ResolveWithSuffix("", SystemDataPath);

    private string ResolveCacheDirectory() => ResolveWithSuffix("cache", SystemCachePath);

    private string ResolveConfigDirectory() => ResolveWithSuffix("config", SystemConfigPath);

    private string ResolveWithSuffix(string suffix, Func<string> systemPath)
    {
        if (EFFECTIVE_HOME != FallbackHome)
            return Path.Combine(EFFECTIVE_HOME, suffix);

        if (Directory.Exists(FallbackHome))
            return Path.Combine(FallbackHome, suffix);

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

    private static string TridentFolderName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dev.dearain.trident" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Trident" : "trident";

    private static string SystemDataPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     TridentFolderName());

    private static string SystemCachePath() =>
        Path.Combine(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.GetTempPath()
                    : Path.Combine(USER_PROFILE, ".cache"),
            TridentFolderName());

    private static string SystemConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     TridentFolderName());

    private static string SystemBrandConfigPath(string brand) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           TridentFolderName(), FormatBrandFolderName(brand))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           FormatBrandFolderName(brand));

    private static string SystemBrandDataPath(string brand) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           TridentFolderName(), FormatBrandFolderName(brand))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           FormatBrandFolderName(brand));

    private static string SystemBrandCachePath(string brand)
    {
        var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.GetTempPath()
                : Path.Combine(USER_PROFILE, ".cache");

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(basePath, TridentFolderName(), FormatBrandFolderName(brand))
            : Path.Combine(basePath, FormatBrandFolderName(brand));
    }

    #endregion

    #region Brand formatting

    private static string FormatBrandFolderName(string brand) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? brand.ToLowerInvariant()
            : brand.Length > 0
                ? char.ToUpperInvariant(brand[0]) + brand[1..]
                : brand;

    #endregion

    #region Home locator

    private static string FallbackHome => Path.Combine(USER_PROFILE, ".trident");

    private static string LocateEffectiveHome()
    {
        var envHome = Environment.GetEnvironmentVariable("TRIDENT_HOME");
        if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
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
            if (!string.IsNullOrWhiteSpace(firstLine) && Path.IsPathRooted(firstLine) && Directory.Exists(firstLine))
                return firstLine;
        }

        return FallbackHome;
    }

    #endregion
}
