using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace TridentCore.Cli;

public static class McpHost
{
    public static async Task<int> RunAsync(IServiceCollection appServices)
    {
        appServices.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(consoleLogOptions =>
            {
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        appServices
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(McpHost).Assembly);

        var hostBuilder = new HostBuilder();
        hostBuilder.ConfigureServices((_, hostServices) =>
        {
            foreach (var descriptor in appServices)
                hostServices.Add(descriptor);
        });

        using var host = hostBuilder.Build();
        try
        {
            await host.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
