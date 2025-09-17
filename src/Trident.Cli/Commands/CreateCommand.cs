using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trident.Cli.Commands;
public class CreateCommand : CreationCommandBase<CreateCommand.Arguments>
{
    public override int Execute(CommandContext context, Arguments settings) => throw new NotImplementedException();

    public class Arguments : CreationArgumentsBase
    {

    }
}
