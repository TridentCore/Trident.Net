namespace Trident.Cli.Services;

public class CliException : Exception
{
    public CliException(string message, int exitCode = ExitCodes.Unknown)
        : base(message) => ExitCode = exitCode;

    public CliException(
        string message,
        Exception innerException,
        int exitCode = ExitCodes.Unknown
    )
        : base(message, innerException) => ExitCode = exitCode;

    public int ExitCode { get; }
}
