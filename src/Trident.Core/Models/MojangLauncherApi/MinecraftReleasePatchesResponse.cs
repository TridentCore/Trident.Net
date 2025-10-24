namespace Trident.Core.Models.MojangLauncherApi;

public record MinecraftReleasePatchesResponse(int Version, IReadOnlyList<MinecraftReleasePatchesResponse.Entry> Entries)
{
    #region Nested type: Entry

    public record Entry(
        string Title,
        string Type,
        string Version,
        string Id,
        DateTimeOffset Date,
        string ContentPath,
        string ShortText,
        Entry.EntryImage Image)
    {
        #region Nested type: EntryImage

        public record EntryImage(string Title, Uri Url);

        #endregion
    }

    #endregion
}
