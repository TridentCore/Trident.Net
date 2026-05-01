using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Services.Instances;

public class LaunchOptions(
    string? brand = null,
    LaunchMode launchMode = LaunchMode.Managed,
    IAccount? account = null,
    (uint, uint)? windowSize = null,
    string? quickConnectAddress = null,
    uint maxMemory = 4096,
    string? additionalArguments = null,
    string? commandWrapperTemplate = null
)
{
    public LaunchMode Mode { get; set; } = launchMode;

    public IAccount? Account { get; set; } = account;
    public uint MaxMemory { get; set; } = maxMemory;
    public (uint, uint) WindowSize { get; set; } = windowSize ?? (1270, 720);
    public string? QuickConnectAddress { get; set; } = quickConnectAddress;
    public string AdditionalArguments { get; set; } = additionalArguments ?? string.Empty;
    public string CommandWrapperTemplate { get; set; } = commandWrapperTemplate ?? string.Empty;

    public string Brand { get; set; } = brand ?? "Trident";
}
