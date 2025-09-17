using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trident.Cli.Commands;
public class CreationArgumentsBase : CommandSettings
{
    [CommandOption("-n|--name <NAME>", isRequired: true)]
    public required string Name { get; set; }
    [CommandOption("-i|--id <ID>", isRequired: true)]
    public required string Id { get; set; }
}
