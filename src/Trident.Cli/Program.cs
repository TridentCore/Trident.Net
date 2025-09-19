using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Trident.Abstractions;
using Trident.Cli;
using Trident.Cli.Services;

#if DEBUG
var env = "Development";
#else
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
#endif

var environment = new SimpleEnvironment { EnvironmentName = env };

var lookup = LookupHome();
var services = new ServiceCollection();

var configurationBuilder = new ConfigurationBuilder();
Startup.ConfigureConfiguration(configurationBuilder, environment);
var configuration = configurationBuilder.Build();
Startup.ConfigureServices(services, configuration, environment);


services.AddSingleton<IConfiguration>(configuration);
services.AddSingleton<IEnvironment>(environment);
services.AddSingleton(lookup);

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);
Startup.ConfigureCommands(app);

return await app.RunAsync(args);

LookupContext LookupHome() => LookupHomeInternal(Environment.CurrentDirectory);

LookupContext LookupHomeInternal(string startDir)
{
    string? home = null;
    string? profile = null;
    var dir = startDir;
    while (dir is not null && Directory.Exists(dir))
    {
        var canidiate = Path.Combine(dir, ".trident");
        if (Directory.Exists(canidiate))
        {
            home = canidiate;
        }

        var found = Path.Combine(dir, "profile.json");
        if (File.Exists(found))
        {
            profile = found;
        }

        dir = Path.GetDirectoryName(dir);
    }

    if (home != null)
    {
        PathDef.HomeLocatorDefault = () => home;
    }

    return new(PathDef.Default.Home) { FoundProfile = profile };
}
