using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions;
using TridentCore.Cli;
using TridentCore.Cli.Services;

#if DEBUG
const bool isDebug = true;
const string env = "Development";
#else
const bool isDebug = false;
string env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
#endif

var environment = new SimpleEnvironment { EnvironmentName = env };
CliInvocation invocation;
try
{
    invocation = CliContext.Parse(args);
}
catch (CliException ex)
{
    WriteStartupError(null, ex, ex.ExitCode);
    return ex.ExitCode;
}

var lookup = LookupHome(invocation.HomeOverride);
var services = new ServiceCollection();

var configurationBuilder = new ConfigurationBuilder();
Startup.ConfigureConfiguration(configurationBuilder, environment);
var configuration = configurationBuilder.Build();
Startup.ConfigureServices(services, configuration, environment, invocation.Context, isDebug);

services.AddSingleton<IConfiguration>(configuration);
services.AddSingleton<IEnvironment>(environment);
services.AddSingleton(lookup);

if (invocation.Context.Mcp)
{
    return await McpHost.RunAsync(services);
}

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);
Startup.ConfigureCommands(app);

try
{
    return await app.RunAsync(invocation.Arguments);
}
catch (CliException ex)
{
    WriteStartupError(invocation.Context, ex, ex.ExitCode);
    return ex.ExitCode;
}
catch (OperationCanceledException ex)
{
    WriteStartupError(invocation.Context, ex, ExitCodes.CANCELED);
    return ExitCodes.CANCELED;
}
catch (CommandAppException ex)
{
    WriteStartupError(invocation.Context, ex, ExitCodes.USAGE);
    return ExitCodes.USAGE;
}
catch (Exception ex)
{
    WriteStartupError(invocation.Context, ex, ExitCodes.UNKNOWN);
    return ExitCodes.UNKNOWN;
}

LookupContext LookupHome(string? homeOverride) =>
    LookupHomeCore(Environment.CurrentDirectory, homeOverride);

LookupContext LookupHomeCore(string startDir, string? homeOverride)
{
    string? home = null;
    string? profile = null;
    var dir = Path.GetFullPath(startDir);
    while (dir is not null && Directory.Exists(dir))
    {
        if (profile is null)
        {
            var found = Path.Combine(dir, "profile.json");
            if (File.Exists(found))
            {
                profile = Path.GetFullPath(found);
            }
        }

        if (home is null)
        {
            var candidate = Path.Combine(dir, ".trident");
            if (Directory.Exists(candidate))
            {
                home = Path.GetFullPath(candidate);
            }
        }

        dir = Path.GetDirectoryName(dir);
    }

    home = Path.GetFullPath(
        homeOverride
            ?? home
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".trident"
            )
    );
    Directory.CreateDirectory(home);

    Environment.SetEnvironmentVariable("TRIDENT_HOME", home);

    return new(home) { FoundProfile = profile };
}

void WriteStartupError(CliContext? context, Exception exception, int exitCode)
{
    var message = string.IsNullOrWhiteSpace(exception.Message)
        ? exception.GetType().Name
        : exception.Message;
    var detail = isDebug || context?.Debug is true ? exception.ToString() : null;

    if (context?.UseStructuredOutput is true)
    {
        Console.Error.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    error = message,
                    detail,
                    exitCode,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
        );
        return;
    }

    var error = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(Console.Error) });
    var body = $"[bold red]{Markup.Escape(message)}[/]";
    if (!string.IsNullOrWhiteSpace(detail))
    {
        body += $"\n\n[grey]{Markup.Escape(detail)}[/]";
    }

    error.Write(
        new Panel(body)
            .Header($"[bold red]ERROR[/] [dim]exit {exitCode}[/]")
            .RoundedBorder()
            .BorderColor(Color.Red)
            .Expand()
    );
}
