namespace Trident.Core.Models.XboxLiveApi;

public record XboxLiveRequest<T>(T Properties, string RelyingParty, string TokenType = "JWT");
