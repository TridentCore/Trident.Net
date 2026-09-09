using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Services.Instances;

public sealed class LaunchOptions(
    string? brand = null,
    LaunchMode launchMode = LaunchMode.Managed,
    IAccount? account = null,
    (uint, uint)? windowSize = null,
    string? quickConnectAddress = null,
    uint maxMemory = 4096,
    string? additionalArguments = null,
    string? commandWrapperTemplate = null)
{
    public LaunchMode Mode { get; init; } = launchMode;

    public IAccount? Account { get; init; } = account;
    public uint MaxMemory { get; init; } = maxMemory;
    public (uint, uint) WindowSize { get; init; } = windowSize ?? (1270, 720);
    public string? QuickConnectAddress { get; init; } = quickConnectAddress;
    public string AdditionalArguments { get; init; } = additionalArguments ?? string.Empty;
    public string CommandWrapperTemplate { get; init; } = commandWrapperTemplate ?? string.Empty;

    public string Brand { get; init; } = brand ?? "Trident";
}
