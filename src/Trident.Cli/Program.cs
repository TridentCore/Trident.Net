using Microsoft.Extensions.Hosting;
using Trident.Cli;

var builder = Host.CreateApplicationBuilder(args);
Startup.ConfigureService(builder.Services, builder.Configuration, builder.Environment);

var host = builder.Build();

host.Run();
