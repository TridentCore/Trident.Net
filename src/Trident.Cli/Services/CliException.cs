namespace Trident.Cli.Services;

public class CliException(string message, int exitCode = ExitCodes.Unknown) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
