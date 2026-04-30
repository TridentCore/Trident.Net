using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions;
using Trident.Cli;
using Trident.Cli.Services;

#if DEBUG
const bool IsDebug = true;
var env = "Development";
#else
const bool IsDebug = false;
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
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
Startup.ConfigureServices(services, configuration, environment, invocation.Context, IsDebug);

services.AddSingleton<IConfiguration>(configuration);
services.AddSingleton<IEnvironment>(environment);
services.AddSingleton(lookup);

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
    WriteStartupError(invocation.Context, ex, ExitCodes.Canceled);
    return ExitCodes.Canceled;
}
catch (CommandAppException ex)
{
    WriteStartupError(invocation.Context, ex, ExitCodes.Usage);
    return ExitCodes.Usage;
}
catch (Exception ex)
{
    WriteStartupError(invocation.Context, ex, ExitCodes.Unknown);
    return ExitCodes.Unknown;
}

LookupContext LookupHome(string? homeOverride) => LookupHomeInternal(Environment.CurrentDirectory, homeOverride);

LookupContext LookupHomeInternal(string startDir, string? homeOverride)
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

    PathDef.HomeLocatorDefault = () => home;
    PathDef.Default = new(home);

    return new(home) { FoundProfile = profile };
}

void WriteStartupError(CliContext? context, Exception exception, int exitCode)
{
    var message = string.IsNullOrWhiteSpace(exception.Message)
        ? exception.GetType().Name
        : exception.Message;
    var detail = IsDebug || context?.Debug is true ? exception.ToString() : null;

    if (context?.UseStructuredOutput is true)
    {
        Console.Error.WriteLine(
            JsonSerializer.Serialize(
                new { error = message, detail, exitCode },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
        );
        return;
    }

    var error = AnsiConsole.Create(
        new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }
    );
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
