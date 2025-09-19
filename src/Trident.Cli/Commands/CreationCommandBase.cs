using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public abstract class CreationCommandBase<T> : Command<T> where T : CreationArgumentsBase { }
