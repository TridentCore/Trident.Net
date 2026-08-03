namespace TridentCore.Core.Models.AtLauncher;

// Subset of ATLauncher's instance.json. The file is a Mojang MinecraftVersion (top-level id, libraries,
// downloads, ...) with an ATL-specific `launcher` block bolted on; only the launcher block and the
// top-level id (the Minecraft version) are needed for migration.
public record AtLauncherInstance(string? Id, AtLauncherInstance.LauncherData? Launcher)
{
    public record LauncherData(string? Name, LoaderVersion? LoaderVersion);

    // loaderVersion: null for vanilla. `type` is a case-insensitive loader name (Forge, Fabric,
    // Quilt, NeoForge, LegacyFabric, Paper, Purpur, ...).
    public record LoaderVersion(string? Version, string? Type);
}
