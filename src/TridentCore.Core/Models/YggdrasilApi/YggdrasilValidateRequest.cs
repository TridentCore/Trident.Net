using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilValidateRequest(string AccessToken, string? ClientToken);
