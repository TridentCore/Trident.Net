using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Refit;
using TridentCore.Abstractions.Lifetimes;
using TridentCore.Core.Clients;
using TridentCore.Core.Lifetimes;
using TridentCore.Core.Services;

namespace TridentCore.Core.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly RefitSettings Dummy = new();

    public static IServiceCollection AddLifetimeRuntime(this IServiceCollection services)
    {
        services.AddSingleton<LifetimeServiceRuntime>();
        return services;
    }

    public static IServiceCollection AddLifetimeService<T>(this IServiceCollection services)
        where T : class, ILifetimeService
    {
        services.AddSingleton<T>();
        services.AddSingleton<ILifetimeService>(sp => sp.GetRequiredService<T>());
        return services;
    }

    public static IServiceCollection AddPrismLauncher(this IServiceCollection services)
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

    public static IServiceCollection AddMojangLauncher(this IServiceCollection services)
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

    public static IServiceCollection AddMicrosoft(this IServiceCollection services)
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
                                    Dummy
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
                    new ProductInfoHeaderValue(
                        "Trident.Net",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    )
                );
            });
        services.AddSingleton<MicrosoftService>();
        return services;
    }

    public static IServiceCollection AddXboxLive(this IServiceCollection services)
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
                    new ProductInfoHeaderValue(
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
                    new ProductInfoHeaderValue(
                        "Trident.Net",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    )
                );
            });
        services.AddSingleton<XboxLiveService>();
        return services;
    }

    public static IServiceCollection AddMinecraft(this IServiceCollection services)
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
                    new ProductInfoHeaderValue(
                        "Trident.Net",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    )
                );
            });
        services.AddSingleton<MinecraftService>();
        return services;
    }

    public static IServiceCollection AddMclogs(this IServiceCollection services)
    {
        services
            .AddRefitClient<IMclogsClient>(_ =>
                new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)))
            )
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new("https://api.mclo.gs");
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(
                        "Trident.Net",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    )
                );
            });

        return services;
    }
}
