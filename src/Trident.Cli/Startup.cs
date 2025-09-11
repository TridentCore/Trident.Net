using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trident.Cli;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
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
            logging.AddConsole();
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
        });

        services.AddMemoryCache();
    }
}
