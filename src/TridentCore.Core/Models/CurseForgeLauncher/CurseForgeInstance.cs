namespace TridentCore.Core.Models.CurseForgeLauncher;

// NOTE: CurseForge App 的 minecraftinstance.json 子集——只含迁移适配器所需字段。
//  完整文件还带 installedAddons/manifest/cachedScans 等；识别重读磁盘 mod 文件，
//  故有意不建模 addon 列表。
public record CurseForgeInstance(string? Name, string? GameVersion, CurseForgeInstance.ModLoader? BaseModLoader)
{
    // NOTE: baseModLoader 在有 loader 时存在且带类型，vanilla 时缺省/null。`type` 是权威 loader 标识，
    //  `name` 携带版本（forge-<v>、fabric-<loaderVer>-<mcVer>、quilt-<v>、neoforge-<v>）。
    public record ModLoader(string? Name, int Type);
}
