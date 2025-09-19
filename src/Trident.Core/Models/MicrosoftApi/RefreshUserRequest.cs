using Refit;
using Trident.Core.Services;

namespace Trident.Core.Models.MicrosoftApi;

public record RefreshUserRequest(
    [property: AliasAs("refresh_token")] string RefreshToken,
    [property: AliasAs("grant_type")] string GrantType = "refresh_token",
    [property: AliasAs("client_id")] string ClientId = MicrosoftService.CLIENT_ID);
