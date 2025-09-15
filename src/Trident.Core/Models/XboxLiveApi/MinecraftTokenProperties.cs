namespace Trident.Core.Models.XboxLiveApi;

public record MinecraftTokenProperties(
    IReadOnlyList<string> UserTokens,
    string SandboxId = "RETAIL");
