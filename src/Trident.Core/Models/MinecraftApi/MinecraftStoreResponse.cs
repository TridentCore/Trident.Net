namespace Trident.Core.Models.MinecraftApi;

public record MinecraftStoreResponse(
    string? Error,
    string? ErrorMessage,
    IReadOnlyList<MinecraftStoreResponse.Item> Items,
    string Signature,
    int KeyId)
{
    #region Nested type: Item

    public record Item(string Name, string Signature);

    #endregion
}
