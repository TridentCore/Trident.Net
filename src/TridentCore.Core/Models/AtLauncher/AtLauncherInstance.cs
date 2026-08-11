namespace TridentCore.Core.Models.AtLauncher;

// NOTE: ATLauncher instance.json 的子集——文件本体是 Mojang MinecraftVersion（顶层 id、libraries、
//  downloads...）外加 ATL 专属 `launcher` 块；迁移只需要 launcher 块与顶层 id（Minecraft 版本）。
public record AtLauncherInstance(string? Id, AtLauncherInstance.LauncherData? Launcher)
{
    public record LauncherData(string? Name, LoaderVersion? LoaderVersion);

    // NOTE: loaderVersion：vanilla 为 null。`type` 是不区分大小写的 loader 名
    //  （Forge、Fabric、Quilt、NeoForge、LegacyFabric、Paper、Purpur...）。
    public record LoaderVersion(string? Version, string? Type);
}
