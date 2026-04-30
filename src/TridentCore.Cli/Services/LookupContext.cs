namespace TridentCore.Cli.Services;

public class LookupContext(string tridentHome)
{
    public string TridentHome { get; } = tridentHome;
    public string? FoundProfile { get; init; }
}
