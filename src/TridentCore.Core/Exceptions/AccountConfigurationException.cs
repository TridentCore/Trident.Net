namespace TridentCore.Core.Exceptions;

public class AccountConfigurationException(string message, Exception? inner = null)
    : AccountException(message, inner)
{ }