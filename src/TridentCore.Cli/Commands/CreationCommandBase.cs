using Spectre.Console.Cli;

namespace TridentCore.Cli.Commands;

public abstract class CreationCommandBase<T> : Command<T>
    where T : CreationArgumentsBase
{ }
