namespace TridentCore.Core.Services.Instances;

public class DeployOptions(bool? fastMode, bool? fullCheckMode)
{
    public bool FastMode { get; set; } = fastMode ?? false;
    public bool FullCheckMode { get; set; } = fullCheckMode ?? false;
}
