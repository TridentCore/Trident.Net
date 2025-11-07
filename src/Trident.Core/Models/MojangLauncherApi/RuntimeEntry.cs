namespace Trident.Core.Models.MojangLauncherApi;

public record RuntimeEntry(
    RuntimeEntry.EntryAvailability Availability,
    RuntimeEntry.EntryManifest Manifest,
    RuntimeEntry.EntryVersion Version)
{
    #region Nested type: EntryAvailability

    public record EntryAvailability(int Group, int Progress);

    #endregion

    #region Nested type: EntryManifest

    public record EntryManifest(string Sha1, long Size, Uri Url);

    #endregion

    #region Nested type: EntryVersion

    public record EntryVersion(string Name, DateTimeOffset Rreleased);

    #endregion
}
