using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Importers;
using TridentCore.Cli.Commands;
using TridentCore.Cli.Commands.Account;
using TridentCore.Cli.Commands.Instance;
using TridentCore.Cli.Commands.Loader;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Commands.Package.Dependency;
using TridentCore.Cli.Commands.Package.Dependent;
using TridentCore.Cli.Commands.Package.Version;
using TridentCore.Cli.Commands.Repository;
using TridentCore.Cli.Services;
using TridentCore.Core.Exporters;
using TridentCore.Core.Extensions;
using TridentCore.Core.Importers;
using TridentCore.Core.Services;

namespace TridentCore.Cli;

public static class Startup
{
    public static void ConfigureConfiguration(IConfigurationBuilder builder, IEnvironment environment) =>
        builder
           .SetBasePath(AppContext.BaseDirectory)
           .AddJsonFile("appsettings.json", true)
           .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", true);

    public static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        SimpleEnvironment environment,
        CliContext cliContext,
        bool isDebug)
    {
        services.AddSingleton(cliContext);
        services.AddSingleton<CliOutput>();
        services.AddSingleton<InstanceContextResolver>();
        services.AddSingleton<TrackerAwaiter>();
        services.AddSingleton<StdinValueReader>();
        services.AddSingleton<BuiltinRepositoryProviderAccessor>();
        services.AddSingleton<UserRepositoryStore>();
        services.AddSingleton<CliRepositoryProviderAccessor>();
        services.AddSingleton<AccountStore>();
        services.AddTransient<IRepositoryProviderAccessor>(sp => sp
                                                              .GetRequiredService<CliRepositoryProviderAccessor>());

        services
           .AddHttpClient()
           .ConfigureHttpClientDefaults(builder => builder
                                                  .ConfigureHttpClient(client => client.BaseAddress =
                                                                           new(configuration["ApiBaseUrl"]
                                                                            ?? "https://api.example.com"))
                                                  .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                                                   {
                                                       UseProxy = false, UseDefaultCredentials = false
                                                   }));

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(isDebug || cliContext.Debug ? LogLevel.Debug :
                                    cliContext.Verbose ? LogLevel.Information : LogLevel.Warning);
        });

        services.AddMemoryCache();
        services.AddDistributedMemoryCache();

        services
           .AddTransient<IProfileImporter, TridentImporter>()
           .AddTransient<IProfileImporter, CurseForgeImporter>()
           .AddTransient<IProfileImporter, ModrinthImporter>()
           .AddTransient<IProfileExporter, TridentExporter>()
           .AddTransient<IProfileExporter, CurseForgeExporter>()
           .AddTransient<IProfileExporter, ModrinthExporter>()
           .AddLifetimeRuntime()
           .AddPrismLauncher()
           .AddMojangLauncher()
           .AddMicrosoft()
           .AddXboxLive()
           .AddMinecraft()
           .AddMclogs()
           .AddSingleton<ProfileManager>()
           .AddSingleton<RepositoryAgent>()
           .AddSingleton<ImporterAgent>()
           .AddSingleton<ExporterAgent>()
           .AddSingleton<InstanceManager>();
    }

    public static void ConfigureCommands(CommandApp app) =>
        app.Configure(config =>
        {
            config.SetApplicationName("trident");
            config.PropagateExceptions();
            config.AddExample("--json", "package", "list", "--instance", "cherry_picks");
            config.AddExample("--home", "C:/path/to/.trident", "list");
            config.AddExample("--no-interactive", "instance", "delete", "--instance", "cherry_picks", "--yes");

            config.AddBranch("instance",
                             instance =>
                             {
                                 instance.SetDescription("Manage Trident instances.");
                                 instance.AddCommand<CreateCommand>("create").WithDescription("Create an instance.");
                                 instance.AddCommand<InstanceListCommand>("list").WithDescription("List instances.");
                                 instance
                                    .AddCommand<InstanceInspectCommand>("inspect")
                                    .WithDescription("Inspect an instance.");
                                 instance
                                    .AddCommand<InstanceBuildCommand>("build")
                                    .WithDescription("Build an instance.");
                                 instance
                                    .AddCommand<InstanceImportCommand>("import")
                                    .WithDescription("Import an instance pack.");
                                 instance
                                    .AddCommand<InstanceExportCommand>("export")
                                    .WithDescription("Export an instance pack.");
                                 instance
                                    .AddCommand<InstanceUnlockCommand>("unlock")
                                    .WithDescription("Unlock an instance from its source.");
                                 instance
                                    .AddCommand<InstanceResetCommand>("reset")
                                    .WithDescription("Reset instance build artifacts.");
                                 instance
                                    .AddCommand<InstanceDeleteCommand>("delete")
                                    .WithDescription("Delete an instance.");
                                 instance.AddCommand<InstanceRunCommand>("run").WithDescription("Run an instance.");
                             });

            config.AddCommand<CreateCommand>("create").WithDescription("Shortcut for instance create.");
            config.AddCommand<InstanceImportCommand>("import").WithDescription("Shortcut for instance import.");
            config.AddCommand<InstanceExportCommand>("export").WithDescription("Shortcut for instance export.");
            config.AddCommand<InstanceBuildCommand>("build").WithDescription("Shortcut for instance build.");
            config.AddCommand<InstanceRunCommand>("run").WithDescription("Shortcut for instance run.");
            config.AddCommand<InstanceListCommand>("list").WithDescription("Shortcut for instance list.");
            config.AddCommand<InstanceInspectCommand>("inspect").WithDescription("Shortcut for instance inspect.");

            config.AddBranch("loader",
                             loader =>
                             {
                                 loader.SetDescription("Manage loaders.");
                                 loader.AddCommand<LoaderHelpCommand>("help").WithDescription("Show loader help.");
                                 loader
                                    .AddCommand<LoaderListCommand>("list")
                                    .WithDescription("List supported loaders.");
                                 loader.AddCommand<LoaderGetCommand>("get").WithDescription("Get instance loader.");
                                 loader.AddCommand<LoaderSetCommand>("set").WithDescription("Set instance loader.");
                                 loader.AddBranch("version",
                                                  version =>
                                                  {
                                                      version.SetDescription("Query loader versions.");
                                                      version
                                                         .AddCommand<LoaderVersionListCommand>("list")
                                                         .WithDescription("List loader versions for a Minecraft version.");
                                                  });
                             });

            config.AddBranch("package",
                             package =>
                             {
                                 package.SetDescription("Manage packages.");
                                 package
                                    .AddCommand<PackageListCommand>("list")
                                    .WithDescription("List installed packages.");
                                 package.AddCommand<PackageSearchCommand>("search").WithDescription("Search packages.");
                                 package
                                    .AddCommand<PackageAddCommand>("add")
                                    .WithDescription("Add packages to an instance.");
                                 package
                                    .AddCommand<PackageInspectCommand>("inspect")
                                    .WithDescription("Inspect a package.");
                                 package
                                    .AddCommand<PackageEnableCommand>("enable")
                                    .WithDescription("Enable an installed package.");
                                 package
                                    .AddCommand<PackageDisableCommand>("disable")
                                    .WithDescription("Disable an installed package.");
                                 package.AddBranch("dependency",
                                                   dependency =>
                                                   {
                                                       dependency.SetDescription("Inspect package dependencies.");
                                                       dependency
                                                          .AddCommand<PackageDependencyListCommand>("list")
                                                          .WithDescription("List package dependencies.");
                                                   });
                                 package.AddBranch("dependent",
                                                   dependent =>
                                                   {
                                                       dependent
                                                          .SetDescription("Inspect instance-local reverse dependencies.");
                                                       dependent
                                                          .AddCommand<PackageDependentListCommand>("list")
                                                          .WithDescription("List instance-local dependents.");
                                                   });
                                 package.AddBranch("version",
                                                   version =>
                                                   {
                                                       version.SetDescription("Manage package versions.");
                                                       version
                                                          .AddCommand<PackageVersionListCommand>("list")
                                                          .WithDescription("List package versions.");
                                                       version
                                                          .AddCommand<PackageVersionSetCommand>("set")
                                                          .WithDescription("Set an installed package version.");
                                                   });
                             });

            config.AddCommand<PackageSearchCommand>("search").WithDescription("Shortcut for package search.");
            config.AddCommand<PackageAddCommand>("add").WithDescription("Shortcut for package add.");

            config.AddBranch("repository",
                             repository =>
                             {
                                 repository.SetDescription("Manage repositories.");
                                 repository
                                    .AddCommand<RepositoryHelpCommand>("help")
                                    .WithDescription("Show repository help.");
                                 repository
                                    .AddCommand<RepositoryListCommand>("list")
                                    .WithDescription("List repositories.");
                                 repository
                                    .AddCommand<RepositoryStatusCommand>("status")
                                    .WithDescription("Check repository status.");
                                 repository
                                    .AddCommand<RepositoryAddCommand>("add")
                                    .WithDescription("Add or replace a user repository.");
                                 repository
                                    .AddCommand<RepositoryRemoveCommand>("remove")
                                    .WithDescription("Remove a user repository.");
                             });

            config.AddBranch("account",
                             account =>
                             {
                                 account.SetDescription("Manage accounts.");
                                 account.AddCommand<AccountHelpCommand>("help").WithDescription("Show account help.");
                                 account.AddCommand<AccountListCommand>("list").WithDescription("List accounts.");
                                 account.AddCommand<AccountAddCommand>("add").WithDescription("Add an account.");
                                 account
                                    .AddCommand<AccountRemoveCommand>("remove")
                                    .WithDescription("Remove an account.");
                             });
        });
}
