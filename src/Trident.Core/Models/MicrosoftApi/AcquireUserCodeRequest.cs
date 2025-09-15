using Trident.Core.Services;
using Refit;

namespace Trident.Core.Models.MicrosoftApi;

public record AcquireUserCodeRequest(
    [property: AliasAs("client_id")] string ClientId = MicrosoftService.CLIENT_ID,
    [property: AliasAs("scope")] string Scope = MicrosoftService.SCOPE);
