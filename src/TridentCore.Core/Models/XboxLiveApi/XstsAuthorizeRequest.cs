namespace TridentCore.Core.Models.XboxLiveApi;

public record XstsAuthorizeRequest(
    MinecraftTokenProperties Properties,
    string RelyingParty,
    string TokenType = "JWT");
