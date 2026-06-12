namespace TridentCore.Core.Exceptions;

public class XboxLiveAuthenticationException(
    XboxLiveAuthenticationException.ErrorKind kind,
    string message
) : AccountAuthenticationException(message)
{
    #region ErrorKind enum

    public enum ErrorKind
    {
        Unkown,
        ParentControl,
        UnsupportedRegion,
    }

    #endregion

    public ErrorKind Kind { get; } = kind;
}
