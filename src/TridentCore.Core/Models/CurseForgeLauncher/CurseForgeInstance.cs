namespace TridentCore.Core.Models.CurseForgeLauncher;

// Subset of the CurseForge App's minecraftinstance.json — only the fields the migration adapter needs.
// The full file carries far more (installedAddons, manifest, cachedScans, ...); identification re-reads
// mod files from disk, so the addon list is intentionally not modeled here.
public record CurseForgeInstance(string? Name, string? GameVersion, CurseForgeInstance.ModLoader? BaseModLoader)
{
    // baseModLoader: present and typed when the instance has a loader, absent/null for vanilla.
    // `type` is the authoritative loader identity; `name` carries the version (forge-<v>,
    // fabric-<loaderVer>-<mcVer>, quilt-<v>, neoforge-<v>).
    public record ModLoader(string? Name, int Type);
}
