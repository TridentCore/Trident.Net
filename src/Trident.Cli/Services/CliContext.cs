namespace Trident.Cli.Services;

public sealed class CliContext
{
    private CliContext(
        bool json,
        bool noInteractive,
        bool verbose,
        bool debug,
        bool inputRedirected,
        bool outputRedirected
    )
    {
        Json = json;
        NoInteractive = noInteractive;
        Verbose = verbose;
        Debug = debug;
        InputRedirected = inputRedirected;
        OutputRedirected = outputRedirected;
    }

    public bool Json { get; }
    public bool NoInteractive { get; }
    public bool Verbose { get; }
    public bool Debug { get; }
    public bool InputRedirected { get; }
    public bool OutputRedirected { get; }
    public bool UseStructuredOutput => Json || OutputRedirected;
    public bool IsInteractive => !NoInteractive && !InputRedirected && !OutputRedirected;

    public static CliInvocation Parse(string[] args)
    {
        var remaining = new List<string>();
        string? home = null;
        var json = false;
        var noInteractive = false;
        var verbose = false;
        var debug = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--json":
                    json = true;
                    break;
                case "--no-interactive":
                    noInteractive = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--debug":
                    debug = true;
                    verbose = true;
                    break;
                case "--home":
                    if (i + 1 >= args.Length)
                    {
                        throw new CliException("--home requires a path.", ExitCodes.Usage);
                    }

                    home = args[++i];
                    break;
                default:
                    if (arg.StartsWith("--home=", StringComparison.Ordinal))
                    {
                        home = arg["--home=".Length..];
                    }
                    else
                    {
                        remaining.Add(arg);
                    }

                    break;
            }
        }

        var context = new CliContext(
            json,
            noInteractive,
            verbose,
            debug,
            Console.IsInputRedirected,
            Console.IsOutputRedirected
        );
        return new(remaining.ToArray(), context, home);
    }
}

public sealed record CliInvocation(string[] Arguments, CliContext Context, string? HomeOverride);
