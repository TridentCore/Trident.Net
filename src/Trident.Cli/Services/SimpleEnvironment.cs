using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trident.Cli.Services;

public class SimpleEnvironment: IEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
}
