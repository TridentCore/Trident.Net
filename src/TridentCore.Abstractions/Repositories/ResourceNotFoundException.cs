namespace TridentCore.Abstractions.Repositories;

public class ResourceNotFoundException(string message) : Exception(message) { }
