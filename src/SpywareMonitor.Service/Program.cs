using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpywareMonitor.Service;

var isolatedMode = string.Equals(Environment.GetEnvironmentVariable("SPYWARE_MONITOR_ISOLATED_MODE"), "1", StringComparison.Ordinal);

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = SpywareMonitor.Core.MonitorConstants.ServiceName)
    .ConfigureLogging(logging => { if (!isolatedMode) logging.AddEventLog(settings => settings.SourceName = SpywareMonitor.Core.MonitorConstants.ServiceName); })
    .ConfigureServices(services =>
    {
        services.AddSingleton<MonitorState>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<ProcessSampler>();
        services.AddHostedService<CollectorWorker>();
        if (!isolatedMode) services.AddHostedService<PipeServerWorker>();
    })
    .Build();

await host.RunAsync();
