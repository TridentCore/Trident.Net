namespace Trident.Core.Models.MojangLauncherApi;

public record RuntimeEntry(
    RuntimeEntry.EntryAvailability Availability,
    RuntimeEntry.EntryManifest Manifest,
    RuntimeEntry.EntryVersion Version)
{
    public record EntryAvailability(int Group, int Progress);

    public record EntryManifest(string Sha1, long Size, Uri Url);

    public record EntryVersion(string Name, DateTimeOffset Rreleased);
}
