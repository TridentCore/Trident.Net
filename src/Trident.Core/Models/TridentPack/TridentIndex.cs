using Trident.Abstractions.FileModels;

namespace Trident.Core.Models.TridentPack;

public record TridentIndex(Profile Profile, string Author, TridentIndex.TridentMetadata Metadata)
{
    #region Nested type: TridentMetadata

    public record TridentMetadata(string Version);

    #endregion
}
