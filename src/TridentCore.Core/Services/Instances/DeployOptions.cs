namespace TridentCore.Core.Services.Instances;

public class DeployOptions(bool? fullCheckMode)
{
    public bool FullCheckMode { get; set; } = fullCheckMode ?? false;
}
