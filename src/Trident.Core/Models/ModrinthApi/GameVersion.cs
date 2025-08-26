namespace Trident.Core.Models.ModrinthApi
{
    public readonly record struct GameVersion(string Version, string VersionType, DateTimeOffset Date, bool Major);
}
