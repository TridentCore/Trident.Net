using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli;

public static class Startup
{
    public static void ConfigureConfiguration(IConfigurationBuilder builder, IEnvironment environment)
    {
        builder
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false)
            .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", true);
    }
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration, SimpleEnvironment environment)
    {
        services
            .AddHttpClient()
            .ConfigureHttpClientDefaults(builder => builder
                .ConfigureHttpClient(client => client.BaseAddress = new Uri(configuration["ApiBaseUrl"] ?? "https://api.example.com"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    UseProxy = false,
                    UseDefaultCredentials = false
                }));

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        services.AddMemoryCache();
    }

    public static void ConfigureCommands(CommandApp app)
    {
        app.Configure(config =>
        {
        });
    }
}
