using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using TridentCore.Abstractions.Lifetimes;
using TridentCore.Abstractions.Snapshots;
using TridentCore.Core.Accounts;
using TridentCore.Core.Clients;
using TridentCore.Core.Lifetimes;
using TridentCore.Core.Services;

namespace TridentCore.Core.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly RefitSettings DUMMY = new();

    extension(IServiceCollection services)
    {
        public IServiceCollection AddLifetimeRuntime()
        {
            services.AddSingleton<LifetimeServiceRuntime>();
            return services;
        }

        public IServiceCollection AddLifetimeService<T>()
            where T : class, ILifetimeService
        {
            services.AddSingleton<T>();
            services.AddSingleton<ILifetimeService>(sp => sp.GetRequiredService<T>());
            return services;
        }

        public IServiceCollection AddPrismLauncher()
        {
            services
               .AddRefitClient<IPrismLauncherClient>(_ =>
                                                         new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
                                                    )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(PrismLauncherService.ENDPOINT);
                });

            services.AddSingleton<PrismLauncherService>();

            return services;
        }

        public IServiceCollection AddMojangLauncher()
        {
            services
               .AddRefitClient<IMojangLauncherClient>(_ =>
                                                          new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
                                                     )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(MojangService.LAUNCHER_ENDPOINT);
                });
            services
               .AddRefitClient<IMojangPistonClient>(_ =>
                                                        new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
                                                   )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(MojangService.PISTON_ENDPOINT);
                });

            services.AddSingleton<MojangService>();

            return services;
        }

        public IServiceCollection AddMicrosoft()
        {
            services
               .AddRefitClient<IMicrosoftClient>(_ =>
                                                     new(
                                                         new SystemTextJsonContentSerializer(
                                                              new(JsonSerializerDefaults.Web)
                                                              {
                                                                  PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                                                              }
                                                             )
                                                        )
                                                     {
                                                         ExceptionFactory = async message =>
                                                             message switch
                                                             {
                                                                 { IsSuccessStatusCode: true } => null,
                                                                 { StatusCode: HttpStatusCode.BadRequest } => null,
                                                                 { RequestMessage: not null } => await ApiException
                                                                        .Create(
                                                                                message.RequestMessage,
                                                                                message.RequestMessage.Method,
                                                                                message,
                                                                                DUMMY
                                                                               )
                                                                        .ConfigureAwait(false),
                                                                 _ => new NotImplementedException(),
                                                             },
                                                     }
                                                )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(MicrosoftService.ENDPOINT);
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });
            services.AddSingleton<MicrosoftService>();
            return services;
        }

        public IServiceCollection AddXboxLive()
        {
            services
               .AddRefitClient<IXboxLiveClient>(_ =>
                                                    new(
                                                        new SystemTextJsonContentSerializer(
                                                             new(JsonSerializerDefaults.General) { PropertyNameCaseInsensitive = true }
                                                            )
                                                       )
                                               )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(XboxLiveService.XBOX_ENDPOINT);
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });
            services
               .AddRefitClient<IXboxServiceClient>(_ =>
                                                       new(
                                                           new SystemTextJsonContentSerializer(
                                                                new(JsonSerializerDefaults.General) { PropertyNameCaseInsensitive = true }
                                                               )
                                                          )
                                                  )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(XboxLiveService.XSTS_ENDPOINT);
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });
            services.AddSingleton<XboxLiveService>();
            return services;
        }

        public IServiceCollection AddMinecraft()
        {
            services
               .AddRefitClient<IMinecraftClient>(_ =>
                                                     new(
                                                         new SystemTextJsonContentSerializer(
                                                              new(JsonSerializerDefaults.Web)
                                                              {
                                                                  PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                                                              }
                                                             )
                                                        )
                                                )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(MinecraftService.ENDPOINT);
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });
            services.AddSingleton<MinecraftService>();
            return services;
        }

        public IServiceCollection AddAuthlibInjector()
        {
            services
               .AddRefitClient<IAuthlibInjectorClient>(_ =>
                                                            new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
                                                       )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(AuthlibInjectorService.ENDPOINT);
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });
            services.AddSingleton<AuthlibInjectorService>();
            return services;
        }

        public IServiceCollection AddMclogs()
        {
            services
               .AddRefitClient<IMclogsClient>(_ =>
                                                  new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
                                             )
               .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new("https://api.mclo.gs");
                    client.DefaultRequestHeaders.UserAgent.Add(
                                                               new(
                                                                   "Trident.Net",
                                                                   Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                                                                  )
                                                              );
                });

            return services;
        }

        public IServiceCollection AddYggdrasil()
        {
            services.AddSingleton<YggdrasilService>();
            return services;
        }

        /// <summary>
        ///     Registers all <see cref="IAccountConfigurer" /> implementations and the <see cref="AccountConfigurerAgent" />.
        ///     <para>
        ///         This method depends on the following services being registered first:
        ///         <see cref="MicrosoftService" />, <see cref="XboxLiveService" />,
        ///         <see cref="MinecraftService" />, <see cref="YggdrasilService" />,
        ///         <see cref="AuthlibInjectorService" />.
        ///     </para>
        ///     <para>Call <see cref="AddMicrosoft" />, <see cref="AddXboxLive" />, <see cref="AddMinecraft" />, <see cref="AddYggdrasil" />, and <see cref="AddAuthlibInjector" /> before this method.</para>
        /// </summary>
        public IServiceCollection AddAccountConfigurers()
        {
            services.AddSingleton<IAccountConfigurer, MicrosoftAccountConfigurer>();
            services.AddSingleton<IAccountConfigurer, AuthlibAccountConfigurer>();
            services.AddSingleton<IAccountConfigurer, OfflineAccountConfigurer>();
            services.AddSingleton<IAccountConfigurer, TrialAccountConfigurer>();
            services.AddSingleton<AccountConfigurerAgent>();
            return services;
        }

        public IServiceCollection AddRepositoryInfrastructure()
        {
            services.AddSingleton<RepositoryAuthHandler>();
            services
                .AddHttpClient(RepositoryAgent.CLIENT_NAME)
                .AddHttpMessageHandler<RepositoryAuthHandler>();
            return services;
        }

        public IServiceCollection AddSnapshots<TFactory>() where TFactory : class, ISnapshotStoreFactory
        {
            services.AddSingleton<ISnapshotStoreFactory, TFactory>();
            services.AddSingleton<SnapshotManager>();

            return services;
        }
    }
}
