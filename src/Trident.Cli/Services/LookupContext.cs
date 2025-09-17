using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trident.Cli.Services;
public class LookupContext(string tridentHome)
{
    public string TridentHome { get; } = tridentHome;
    public string? FoundProfile { get; init; }
}
