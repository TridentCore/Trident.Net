namespace Trident.Core.Models.CurseForgePack;

public record Manifest(
    Manifest.MinecraftModel Minecraft,
    string ManifestType,
    int ManifestVersion,
    string Name,
    string Version,
    string Author,
    IReadOnlyList<Manifest.FileModel> Files,
    string Overrides)
{
    #region Nested type: FileModel

    // ReSharper disable InconsistentNaming
    public record FileModel(uint ProjectID, uint FileID, bool Required);
    // ReSharper restore InconsistentNaming

    #endregion

    #region Nested type: MinecraftModel

    public record MinecraftModel(string Version, IReadOnlyList<MinecraftModel.ModLoaderModel> ModLoaders)
    {
        #region Nested type: ModLoaderModel

        public record ModLoaderModel(string Id, bool Primary);

        #endregion
    }

    #endregion
}
