using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trident.Cli.Services;

public interface IEnvironment
{
    string EnvironmentName { get; }
}
