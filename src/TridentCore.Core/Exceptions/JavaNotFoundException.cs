namespace TridentCore.Core.Exceptions;

public class JavaNotFoundException : Exception
{
    public uint MajorVersion { get; }

    public JavaNotFoundException(uint majorVersion)
        : base($"Jre version {majorVersion} not found") => MajorVersion = majorVersion;

    public JavaNotFoundException(string message, Exception? inner = null)
        : base(message, inner) { }
}
