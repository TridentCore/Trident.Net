using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilRefreshRequest(
    string AccessToken,
    string ClientToken,
    bool RequestUser,
    YggdrasilGameProfile? SelectedProfile = null);
