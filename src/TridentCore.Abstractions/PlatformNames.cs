using System;
using System.Runtime.InteropServices;

namespace TridentCore.Abstractions;

public sealed record PlatformNames(string Linux, string Windows, string MacOS)
{
    public string Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacOS
      : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Windows
      : Linux;
}
