namespace TridentCore.Core.Models.MojangLauncherApi;

public record MinecraftNewsResponse(int Version, IReadOnlyList<MinecraftNewsResponse.Entry> Entries)
{
    #region Nested type: Entry

    public record Entry(
        string Title,
        string? Tag,
        string Category,
        DateOnly Date,
        string Text,
        Entry.EntryImage PlayPageImage,
        Entry.EntryImage NewsPageImage,
        Uri ReadMoreLink,
        bool CardBorder,
        string ArticleBody,
        IReadOnlyList<string> NewsType,
        string Id,
        bool? NeedsTranslation)
    {
        #region Nested type: EntryImage

        public record EntryImage(string Title, Uri Url, EntryImage.ImageDimensions? Dimensions = null)
        {
            #region Nested type: ImageDimensions

            public record ImageDimensions(int Width, int Height);

            #endregion
        }

        #endregion
    }

    #endregion
}
