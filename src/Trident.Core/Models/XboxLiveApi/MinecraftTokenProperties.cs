namespace Trident.Core.Models.XboxLiveApi;

public readonly record struct MinecraftTokenProperties(
    IReadOnlyList<string> UserTokens,
    string SandboxId = "RETAIL");
