using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkIntelligence.MonitoringService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = NetworkIntelligence.Contracts.ServiceProtocol.WindowsServiceName);

var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkIntelligence", "Service");
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new ServiceLoggerProvider(logDirectory));

builder.Services.AddSingleton<EtwCollector>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EtwCollector>());
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
await host.RunAsync();
