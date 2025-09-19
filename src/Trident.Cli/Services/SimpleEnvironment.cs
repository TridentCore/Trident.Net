namespace Trident.Cli.Services;

public class SimpleEnvironment : IEnvironment
{
    #region IEnvironment Members

    public string EnvironmentName { get; set; } = "Production";

    #endregion
}
