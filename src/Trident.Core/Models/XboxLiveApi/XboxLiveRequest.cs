namespace Trident.Core.Models.XboxLiveApi
{
    public readonly record struct XboxLiveRequest<T>(T Properties, string RelyingParty, string TokenType = "JWT");
}
