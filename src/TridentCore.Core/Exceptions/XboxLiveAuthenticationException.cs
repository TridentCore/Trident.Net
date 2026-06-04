namespace TridentCore.Core.Exceptions;

public class XboxLiveAuthenticationException(
    XboxLiveAuthenticationException.ErrorKind kind,
    string message
) : AccountAuthenticationException(message)
{
    #region ErrorKind enum

    public enum ErrorKind
    {
        UNKNOWN,
        PARENT_CONTROL,
        UNSUPPORTED_REGION,
    }

    #endregion

    public ErrorKind Kind { get; } = kind;
}
