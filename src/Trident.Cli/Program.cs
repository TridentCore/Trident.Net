using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Trident.Cli;
using Trident.Cli.Commands;

namespace Trident.Cli;

public class SimpleHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = "Trident.Cli";
    public string EnvironmentName { get; set; }
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());

    public SimpleHostEnvironment(string envName)
    {
        EnvironmentName = envName;
    }
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        // Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Hosting environment
        var environment = new SimpleHostEnvironment("Development");

        // Configure services
        Startup.ConfigureServices(services, configuration, environment);

        // Add logging
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        var registrar = new MicrosoftDependencyInjectionTypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.AddCommand<RootCommand>("default");
            config.AddBranch<ManageBranch.Settings>("manage", manage =>
            {
                manage.AddCommand<ManageListCommand>("list");
                manage.AddCommand<ManageAddCommand>("add");
            });
        });

        return await app.RunAsync(args);
    }
}
