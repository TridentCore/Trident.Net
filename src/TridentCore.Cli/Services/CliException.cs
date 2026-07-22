namespace TridentCore.Cli.Services;

public class CliException : Exception
{
    public CliException(string message, int exitCode = ExitCodes.UNKNOWN) : base(message) => ExitCode = exitCode;

    public CliException(string message, Exception innerException, int exitCode = ExitCodes.UNKNOWN) :
        base(message, innerException) =>
        ExitCode = exitCode;

    public int ExitCode { get; }
}
