using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Trident.Core.Clients;
using Trident.Core.Services;
using Refit;

namespace Trident.Core.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly RefitSettings Dummy = new();

    public static IServiceCollection AddPrismLauncher(this IServiceCollection services)
    {
        services
           .AddRefitClient<
                IPrismLauncherClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web))))
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
           .AddRefitClient<
                IMojangLauncherClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web))))
           .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new(MojangLauncherService.ENDPOINT);
            });

        services.AddSingleton<MojangLauncherService>();

        return services;
    }

    public static IServiceCollection AddMicrosoft(this IServiceCollection services)
    {
        services
           .AddRefitClient<
                IMicrosoftClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)
                                  {
                                      PropertyNamingPolicy =
                                          JsonNamingPolicy
                                             .SnakeCaseLower
                                  }))
                                  {
                                      ExceptionFactory = async message => message switch
                                      {
                                          { IsSuccessStatusCode: true } => null,
                                          { StatusCode: HttpStatusCode.BadRequest } => null,
                                          { RequestMessage: not null } => await ApiException
                                                                             .Create(message.RequestMessage,
                                                                                  message.RequestMessage.Method,
                                                                                  message,
                                                                                  Dummy)
                                                                             .ConfigureAwait(false),
                                          _ => new NotImplementedException()
                                      }
                                  })
           .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new(MicrosoftService.ENDPOINT);
                client.DefaultRequestHeaders.Add("User-Agent",
                                                 $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}");
            });
        services.AddSingleton<MicrosoftService>();
        return services;
    }

    public static IServiceCollection AddXboxLive(this IServiceCollection services)
    {
        services
           .AddRefitClient<
                IXboxLiveClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            })))
           .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new(XboxLiveService.XBOX_ENDPOINT);
                client.DefaultRequestHeaders.Add("User-Agent",
                                                 $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}");
            });
        services
           .AddRefitClient<
                IXboxServiceClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true
            })))
           .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new(XboxLiveService.XSTS_ENDPOINT);
                client.DefaultRequestHeaders.Add("User-Agent",
                                                 $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}");
            });
        services.AddSingleton<XboxLiveService>();
        return services;
    }

    public static IServiceCollection AddMinecraft(this IServiceCollection services)
    {
        services
           .AddRefitClient<
                IMinecraftClient>(_ => new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy
                       .SnakeCaseLower
            })))
           .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new(MinecraftService.ENDPOINT);
                client.DefaultRequestHeaders.Add("User-Agent",
                                                 $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}");
            });
        services.AddSingleton<MinecraftService>();
        return services;
    }
}
