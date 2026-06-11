namespace TridentCore.Core.Exceptions;

public class AccountException(string message, Exception? inner = null)
    : Exception(message, inner)
{ }