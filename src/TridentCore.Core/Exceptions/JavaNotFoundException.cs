namespace TridentCore.Core.Exceptions;

public class JavaNotFoundException : Exception
{
    public JavaNotFoundException(uint majorVersion) : base($"Jre version {majorVersion} not found") =>
        MajorVersion = majorVersion;

    public JavaNotFoundException(string message, Exception? inner = null) : base(message, inner) { }

    public uint MajorVersion { get; }
}
