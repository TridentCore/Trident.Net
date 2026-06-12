using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilAuthenticateResponse(
    string AccessToken,
    string ClientToken,
    IReadOnlyList<YggdrasilGameProfile>? AvailableProfiles,
    YggdrasilGameProfile? SelectedProfile);
