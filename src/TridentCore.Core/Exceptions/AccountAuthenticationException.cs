namespace TridentCore.Core.Exceptions;

public class AccountAuthenticationException(string message, Exception? inner = null)
    : Exception(message, inner)
{ }
